using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using KubaToolKit.Modules.StepFunctions.Models;
using System.Reflection;
using System.Text.Json;

namespace KubaToolKit.Modules.StepFunctions;

public class StepFunctionsService
{
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
