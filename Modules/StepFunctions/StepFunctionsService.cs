using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.Runtime.CredentialManagement;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using KubaToolKit.Modules.StepFunctions.Models;
using System.Reflection;
using System.Text.Json;

namespace KubaToolKit.Modules.StepFunctions;

/// Levée quand une state machine EXPRESS n'a pas de journalisation
/// CloudWatch Logs configurée : dans ce cas, ni l'API Step Functions
/// (non supportée pour EXPRESS) ni la reconstruction via les logs ne
/// peuvent fournir la liste des exécutions.
public class ExpressLoggingNotConfiguredException : Exception
{
    public ExpressLoggingNotConfiguredException(string message) : base(message)
    {
    }
}

public class StepFunctionsService
{
    // Fenêtre de recherche dans les logs CloudWatch pour les state
    // machines EXPRESS : pas de bornes de date exposées dans l'UI pour ce
    // module, on se limite donc à un passé récent raisonnable.
    private const int ExpressLogsLookbackDays = 7;
    private Amazon.Runtime.AWSCredentials
        GetCredentials(
            string profile)
    {
        var chain =
            new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(
                profile,
                out var credentials))
        {
            throw new Exception(
                $"Unable to load AWS profile '{profile}'");
        }

        return credentials;
    }

    public async Task<List<StateMachineItem>>
    ListStateMachines(
        string profile,
        CancellationToken cancellationToken = default)
    {
        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonStepFunctionsClient(
                credentials,
                RegionEndpoint.EUWest3);

        var items =
            new List<StateMachineItem>();

        string? nextToken = null;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var response =
                await client.ListStateMachinesAsync(
                    new ListStateMachinesRequest
                    {
                        NextToken = nextToken
                    },
                    cancellationToken);

            if (response.StateMachines != null)
            {
                items.AddRange(
                    response.StateMachines.Select(m =>
                        new StateMachineItem
                        {
                            Name = m.Name,
                            Arn = m.StateMachineArn,
                            Type = m.Type?.Value ?? ""
                        }));
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return items
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<List<ExecutionItem>>
    ListExecutions(
        string profile,
        string stateMachineArn,
        CancellationToken cancellationToken = default)
    {
        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonStepFunctionsClient(
                credentials,
                RegionEndpoint.EUWest3);

        var items =
            new List<ExecutionItem>();

        string? nextToken = null;

        // Se limiter à un historique récent raisonnable plutôt que de
        // paginer sur potentiellement des dizaines de milliers d'exécutions.
        const int MaxExecutions = 200;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var response =
                await client.ListExecutionsAsync(
                    new ListExecutionsRequest
                    {
                        StateMachineArn = stateMachineArn,
                        MaxResults = 100,
                        NextToken = nextToken
                    },
                    cancellationToken);

            if (response.Executions != null)
            {
                items.AddRange(
                    response.Executions.Select(x =>
                        new ExecutionItem
                        {
                            Name = x.Name,
                            Arn = x.ExecutionArn,
                            Status = x.Status?.Value ?? "",
                            StartDate = x.StartDate,
                            StopDate = x.StopDate
                        }));
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken)
            && items.Count < MaxExecutions);

        return items
            .OrderByDescending(x => x.StartDate)
            .ToList();
    }

    /// AWS Step Functions ne conserve pas la liste des exécutions des state
    /// machines EXPRESS (ListExecutions/GetExecutionHistory ne les
    /// supportent pas) ; la console AWS les retrouve en interrogeant les
    /// logs CloudWatch de la state machine, si la journalisation est
    /// activée. On fait de même ici.
    public async Task<List<ExecutionItem>>
    ListExpressExecutionsFromLogs(
        string profile,
        string stateMachineArn,
        CancellationToken cancellationToken = default)
    {
        var logGroupIdentifier =
            await GetLoggingDestination(
                profile,
                stateMachineArn,
                cancellationToken);

        if (logGroupIdentifier == null)
        {
            throw new ExpressLoggingNotConfiguredException(
                "Aucune journalisation CloudWatch Logs n'est configurée sur cette state machine EXPRESS. " +
                "Step Functions ne conserve la liste de ses exécutions que si la journalisation est activée " +
                "(onglet \"Journalisation\" dans la console AWS).");
        }

        var rawMessages =
            await QueryExpressLogs(
                profile,
                logGroupIdentifier,
                "filter type in [\"ExecutionStarted\",\"ExecutionSucceeded\",\"ExecutionFailed\",\"ExecutionAborted\",\"ExecutionTimedOut\"]",
                cancellationToken);

        var executions =
            new Dictionary<string, ExecutionItem>();

        foreach (var raw in rawMessages)
        {
            JsonElement root;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            var executionArn = GetStringProperty(root, "execution_arn");
            var type = GetStringProperty(root, "type");

            if (executionArn == null || type == null)
            {
                continue;
            }

            if (!executions.TryGetValue(executionArn, out var item))
            {
                item = new ExecutionItem
                {
                    Arn = executionArn,
                    Name = executionArn.Split(':').Last(),
                    Status = "RUNNING",
                    LogGroupIdentifier = logGroupIdentifier
                };

                executions[executionArn] = item;
            }

            var timestamp = ParseEventTimestamp(root);

            switch (type)
            {
                case "ExecutionStarted":
                    item.StartDate = timestamp;
                    break;

                case "ExecutionSucceeded":
                    item.Status = "SUCCEEDED";
                    item.StopDate = timestamp;
                    break;

                case "ExecutionFailed":
                    item.Status = "FAILED";
                    item.StopDate = timestamp;
                    break;

                case "ExecutionAborted":
                    item.Status = "ABORTED";
                    item.StopDate = timestamp;
                    break;

                case "ExecutionTimedOut":
                    item.Status = "TIMED_OUT";
                    item.StopDate = timestamp;
                    break;
            }
        }

        return executions.Values
            .OrderByDescending(x => x.StartDate)
            .ToList();
    }

    /// Équivalent de GetExecutionHistory pour les state machines EXPRESS,
    /// reconstruit à partir des mêmes logs CloudWatch que
    /// ListExpressExecutionsFromLogs (voir sa remarque ci-dessus).
    public async Task<List<HistoryEventItem>>
    GetExpressExecutionHistoryFromLogs(
        string profile,
        string logGroupIdentifier,
        string executionArn,
        CancellationToken cancellationToken = default)
    {
        var escapedArn =
            executionArn.Replace("\"", "\\\"");

        var rawMessages =
            await QueryExpressLogs(
                profile,
                logGroupIdentifier,
                $"filter execution_arn = \"{escapedArn}\"",
                cancellationToken);

        var items =
            new List<HistoryEventItem>();

        foreach (var raw in rawMessages)
        {
            JsonElement root;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            long.TryParse(
                GetStringProperty(root, "id"),
                out var id);

            string step = "";
            string resource = "";
            string detailsJson = raw;

            if (TryGetPropertyCaseInsensitive(root, "details", out var details))
            {
                step = GetStringProperty(details, "name") ?? "";
                resource = GetStringProperty(details, "resource") ?? "";

                detailsJson =
                    JsonSerializer.Serialize(
                        details,
                        new JsonSerializerOptions { WriteIndented = true });
            }

            items.Add(
                new HistoryEventItem
                {
                    Id = id,
                    Type = GetStringProperty(root, "type") ?? "",
                    Step = step,
                    Resource = resource,
                    Timestamp = ParseEventTimestamp(root),
                    DetailsJson = detailsJson
                });
        }

        return items
            .OrderBy(x => x.Id)
            .ToList();
    }

    private async Task<string?>
    GetLoggingDestination(
        string profile,
        string stateMachineArn,
        CancellationToken cancellationToken)
    {
        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonStepFunctionsClient(
                credentials,
                RegionEndpoint.EUWest3);

        var response =
            await client.DescribeStateMachineAsync(
                new DescribeStateMachineRequest
                {
                    StateMachineArn = stateMachineArn
                },
                cancellationToken);

        var logGroupArn =
            response.LoggingConfiguration?.Destinations?
                .Select(d => d.CloudWatchLogsLogGroup?.LogGroupArn)
                .FirstOrDefault(arn => !string.IsNullOrEmpty(arn));

        if (logGroupArn == null)
        {
            return null;
        }

        // Le format renvoyé se termine par ":*" (toutes les log streams) ;
        // StartQueryRequest.LogGroupIdentifiers attend l'ARN sans ce suffixe.
        return logGroupArn.EndsWith(":*")
            ? logGroupArn[..^2]
            : logGroupArn;
    }

    private async Task<List<string>>
    QueryExpressLogs(
        string profile,
        string logGroupIdentifier,
        string filterExpression,
        CancellationToken cancellationToken)
    {
        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonCloudWatchLogsClient(
                credentials,
                RegionEndpoint.EUWest3);

        var now =
            DateTimeOffset.UtcNow;

        var startResponse =
            await client.StartQueryAsync(
                new StartQueryRequest
                {
                    LogGroupIdentifiers = new List<string> { logGroupIdentifier },
                    StartTime = now.AddDays(-ExpressLogsLookbackDays).ToUnixTimeSeconds(),
                    EndTime = now.ToUnixTimeSeconds(),
                    QueryString = $"fields @message | {filterExpression} | sort @timestamp asc | limit 1000"
                },
                cancellationToken);

        GetQueryResultsResponse results;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            await Task.Delay(500, cancellationToken);

            results =
                await client.GetQueryResultsAsync(
                    new GetQueryResultsRequest
                    {
                        QueryId = startResponse.QueryId
                    },
                    cancellationToken);
        }
        while (results.Status == QueryStatus.Running
            || results.Status == QueryStatus.Scheduled);

        return results.Results
            .Select(row => row.FirstOrDefault(f => f.Field == "@message")?.Value)
            .Where(value => value != null)
            .Select(value => value!)
            .ToList();
    }

    private static bool
    TryGetPropertyCaseInsensitive(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string?
    GetStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static DateTime?
    ParseEventTimestamp(
        JsonElement element)
    {
        var raw = GetStringProperty(element, "event_timestamp");

        return raw != null && long.TryParse(raw, out var epochMs)
            ? DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime
            : null;
    }

    public async Task<List<HistoryEventItem>>
    GetExecutionHistory(
        string profile,
        string executionArn,
        CancellationToken cancellationToken = default)
    {
        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonStepFunctionsClient(
                credentials,
                RegionEndpoint.EUWest3);

        var items =
            new List<HistoryEventItem>();

        string? nextToken = null;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var response =
                await client.GetExecutionHistoryAsync(
                    new GetExecutionHistoryRequest
                    {
                        ExecutionArn = executionArn,
                        MaxResults = 1000,
                        NextToken = nextToken
                    },
                    cancellationToken);

            if (response.Events != null)
            {
                items.AddRange(
                    response.Events.Select(BuildHistoryEventItem));
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return items
            .OrderBy(x => x.Id)
            .ToList();
    }

    private HistoryEventItem
    BuildHistoryEventItem(
        HistoryEvent evt)
    {
        var (step, resource, detailsJson) =
            ExtractDetails(evt);

        return new HistoryEventItem
        {
            Id = evt.Id ?? 0,
            Type = evt.Type?.Value ?? "",
            Step = step ?? "",
            Resource = resource ?? "",
            Timestamp = evt.Timestamp,
            DetailsJson = detailsJson ?? "{}"
        };
    }

    /// HistoryEvent expose une quarantaine de propriétés "XxxEventDetails"
    /// (une par valeur possible de Type) dont une seule est renseignée à la
    /// fois ; plutôt que coder tous les cas, on la retrouve par réflexion.
    private (string? Step, string? Resource, string? DetailsJson)
    ExtractDetails(
        HistoryEvent evt)
    {
        object? details =
            typeof(HistoryEvent)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name.EndsWith("EventDetails"))
                .Select(p => p.GetValue(evt))
                .FirstOrDefault(v => v != null);

        if (details == null)
        {
            return (null, null, null);
        }

        string? step = details switch
        {
            StateEnteredEventDetails d => d.Name,
            StateExitedEventDetails d => d.Name,
            _ => null
        };

        string? resource = details switch
        {
            TaskScheduledEventDetails d => d.Resource,
            TaskStartedEventDetails d => d.Resource,
            TaskSucceededEventDetails d => d.Resource,
            TaskFailedEventDetails d => d.Resource,
            LambdaFunctionScheduledEventDetails d => d.Resource,
            ActivityScheduledEventDetails d => d.Resource,
            _ => null
        };

        string? json;

        try
        {
            json =
                JsonSerializer.Serialize(
                    details,
                    details.GetType(),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition =
                            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
        }
        catch
        {
            json = details.ToString();
        }

        return (step, resource, json);
    }
}
