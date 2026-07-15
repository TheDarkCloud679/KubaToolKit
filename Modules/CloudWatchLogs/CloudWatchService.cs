using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.CloudWatchLogs.Models;
using KubaToolKit.Shared.Services;

namespace KubaToolKit.Modules.CloudWatchLogs;

public class CloudWatchService
{
    private const int
        MaxLogGroupsPerQuery = 30;
    private static readonly
        Dictionary<string, List<string >> LogGroupCache = new();

    public async Task<List<string>>
        GetLogGroups(
            string profileName)
    {
        if (LogGroupCache.TryGetValue(
                profileName,
                out var cached))
        {
            Logger.Debug(
                $"CloudWatchService: log groups pour '{profileName}' servis depuis le cache ({cached.Count}).");

            return cached;
        }

        Logger.Debug($"CloudWatchService: chargement des log groups pour '{profileName}'.");

        var chain = new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(
        profileName,
        out var credentials))
        {
            Logger.Error($"CloudWatchService: profil AWS introuvable '{profileName}'.");

            throw new Exception($"Profil AWS introuvable : {profileName}");
        }

        using var client =
            new AmazonCloudWatchLogsClient(
                credentials,
                RegionEndpoint.EUWest3);

        var result = new List<string>();
        string? nextToken = null;
        var prefix = BuildPrefixFromProfile(profileName);

        try
        {
            do
            {
                var request = new DescribeLogGroupsRequest
                    {
                        NextToken = nextToken
                    };

                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    request.LogGroupNamePrefix = prefix;
                }

                var response =
                    await client.DescribeLogGroupsAsync(request);

                result.AddRange(
                    response.LogGroups
                        .Select(x =>
                            x.LogGroupName));

                nextToken = response.NextToken;
            }
            while (nextToken != null);
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"CloudWatchService: échec du chargement des log groups pour '{profileName}'.",
                ex);

            throw;
        }

        result =
            result
                .Distinct()
                .OrderBy(x => x)
                .ToList();

        LogGroupCache[profileName] = result;

        Logger.Info(
            $"CloudWatchService: {result.Count} log group(s) chargé(s) pour '{profileName}'.");

        return result;
    }

    public string
        BuildQuery(string searchTerm)
    {
        // Aucun filtre
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return
                $"""
                fields @timestamp, @message, @logStream, @log
                | sort @timestamp desc
                | limit 10000
                """;
        }

        string escapedSearch =
            searchTerm
                .Replace("/", "\\/")
                .Replace("\"", "\\\"");

        string filterClause;
        switch (searchTerm)
        {
            case "4XX":

                escapedSearch = @"""status""\s*:\s*""4\d\d""";
                filterClause = $"@message like /{escapedSearch}/";
                break;

            case "5XX":
                escapedSearch = @"""status""\s*:\s*""5\d\d""";

                filterClause = $"@message like /{escapedSearch}/";
                break;

            case "Status Code":
                escapedSearch = @"""status""\s*:\s*""(4\d\d|5\d\d)""";
                filterClause = $"@message like /{escapedSearch}/";
                break;

            case "ERROR":
                escapedSearch = @"(?i)(error|exception|fatal|fail|failed)";
                filterClause = $"@message like /{escapedSearch}/";
                break;

            case "TimeOut":
                escapedSearch = @"(?i)(timeout|timed out|time out|request timeout)";
                filterClause = $"@message like /{escapedSearch}/";
                break;

            default:
                var safeSearch = searchTerm.Replace("'", "\\'");
                filterClause = $"@message like '{safeSearch}'";
                break;
        }

        return
            $"""
            fields @timestamp, @message, @logStream, @log
            | filter {filterClause}
            | sort @timestamp desc
            | limit 10000
            """;
    }

    public async Task<List<LogEntry>>
    SearchLogs(
        string profile,
        string searchText,
        DateTime? startDate,
        string startTime,
        DateTime? endDate,
        string endTime,
        List<string> selectedLogGroups,
        IProgress<int>? progress = null,
        string? customQuery = null,
        CancellationToken
            cancellationToken =
                default)
    {
        Logger.Debug(
            $"CloudWatchService: recherche '{searchText}' sur {selectedLogGroups.Count} log group(s) explicite(s) (profil '{profile}').");

        var chain = new CredentialProfileStoreChain();
        if (!chain.TryGetAWSCredentials(profile, out var credentials))
        {
            Logger.Error($"CloudWatchService: profil AWS introuvable '{profile}'.");

            throw new Exception($"Profil AWS introuvable : {profile}");
        }

        using var clientLogs = new AmazonCloudWatchLogsClient(credentials, RegionEndpoint.EUWest3);
        var logGroups =
            selectedLogGroups.Any()
                ? selectedLogGroups
                : await GetLogGroups(
                   profile);

        var (
            startUnix,
            endUnix) =
            BuildTimeRange(
                startDate,
                startTime,
                endDate,
                endTime);

        var chunks =
            logGroups
                .Chunk(
                    MaxLogGroupsPerQuery)
                .ToList();
        var mergedResults =
            new List<LogEntry>();

        var totalChunks =
            chunks.Count;

        var currentChunk =
            0;

        foreach (var chunk
                 in chunks)
        {
            currentChunk++;

            var query =
                !string.IsNullOrWhiteSpace(
                    customQuery)
                    ? customQuery
                    : BuildQuery(
                        searchText);

            try
            {
                var result =
                    await ExecuteQuery(
                        clientLogs,
                        chunk.ToList(),
                        query,
                        startUnix,
                        endUnix,
                        currentChunk,
                        totalChunks,
                        progress,
                        cancellationToken);

                mergedResults
                    .AddRange(
                        result);
            }
            catch (AmazonCloudWatchLogsException ex)
            when (
                ex.Message.Contains(
                    "retention settings",
                    StringComparison.OrdinalIgnoreCase)
                ||
                ex.Message.Contains(
                    "creation time",
                    StringComparison.OrdinalIgnoreCase))
            { Logger.Debug($"CloudWatchService: chunk {currentChunk} ignoré ({ex.Message})."); }
        }

        progress?.Report(100);

        Logger.Info(
            $"CloudWatchService: recherche '{searchText}' terminée, {mergedResults.Count} résultat(s) sur {totalChunks} chunk(s).");

        return mergedResults.OrderByDescending( x => x.Timestamp ).ToList();
    }

    private async Task<List<LogEntry>>
ExecuteQuery(
    AmazonCloudWatchLogsClient client,
        List<string> logGroups, string query, long startUnix, long endUnix, int currentChunk, int totalChunks, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Logger.Debug(
            $"CloudWatchService: query chunk {currentChunk}/{totalChunks}, "
            + $"{logGroups.Count} log group(s), "
            + $"{DateTimeOffset.FromUnixTimeSeconds(startUnix):yyyy-MM-dd HH:mm:ss} -> "
            + $"{DateTimeOffset.FromUnixTimeSeconds(endUnix):yyyy-MM-dd HH:mm:ss} :{Environment.NewLine}{query}");

        var queryResponse =
    await client
        .StartQueryAsync(
            new StartQueryRequest
            {
                LogGroupNames = logGroups,
                StartTime = startUnix,
                EndTime = endUnix,
                QueryString = query
            },
            cancellationToken);

        GetQueryResultsResponse
            results;
        do
        {
            cancellationToken
           .ThrowIfCancellationRequested();
            await Task.Delay(1000, cancellationToken);
            results =
     await client
         .GetQueryResultsAsync(
             new GetQueryResultsRequest
             {
                 QueryId =
                     queryResponse.QueryId
             },
             cancellationToken);

            var percent = currentChunk * 100 / totalChunks;
            progress?.Report(percent);
        }
        while (
    results.Status == QueryStatus.Running
    ||
    results.Status == QueryStatus.Scheduled);
        Logger.Debug(
            $"CloudWatchService: chunk {currentChunk}/{totalChunks} -> {results.Status}, "
            + $"{results.Results.Count} ligne(s).");

        return results.Results
            .Select(row =>
                new LogEntry
                {
                    Timestamp =
                        row.FirstOrDefault(
                            x =>
                                x.Field == "@timestamp")
                        ?.Value ?? "",

                    LogGroup =
                        row.FirstOrDefault(
                            x =>
                                x.Field == "@log")
                        ?.Value ?? "",

                    Message =
                        row.FirstOrDefault(
                            x =>
                                x.Field == "@message")
                        ?.Value ?? ""
                })
            .ToList();
    }

    private string
       BuildPrefixFromProfile(string profileName)
    {
        // D�sactive le filtre
        return "";
    }

    private (
        long StartUnix,
        long EndUnix)
        BuildTimeRange(
            DateTime? startDate, string startTime, DateTime? endDate, string endTime)
    {
        var start = startDate!.Value.Date + TimeSpan.Parse(startTime);
        var end = endDate!.Value.Date + TimeSpan.Parse(endTime);

        return
        (
            new DateTimeOffset(start).ToUnixTimeSeconds(),
            new DateTimeOffset(end).ToUnixTimeSeconds()
        );
    }
}