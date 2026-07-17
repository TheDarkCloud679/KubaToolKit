using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SQS;
using Amazon.SQS.Model;
using KubaToolKit.Modules.Sqs.Models;
using KubaToolKit.Shared.Services;

namespace KubaToolKit.Modules.Sqs;

public class SqsService
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

    public async Task<List<SqsQueueItem>>
    ListQueuesWithCounts(
        string profile,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug($"SqsService: loading queues (profile '{profile}').");

        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonSQSClient(
                credentials,
                RegionEndpoint.EUWest3);

        var queueUrls =
            new List<string>();

        string? nextToken =
            null;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var response =
                await client.ListQueuesAsync(
                    new ListQueuesRequest
                    {
                        MaxResults = 1000,
                        NextToken = nextToken
                    });

            if (response.QueueUrls != null)
            {
                queueUrls.AddRange(
                    response.QueueUrls);
            }

            nextToken =
                response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        var results =
            new List<SqsQueueItem>();

        int processed = 0;

        foreach (var url
                 in queueUrls.OrderBy(x => x))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            processed++;

            progress?.Report(
                (int)(processed * 100.0
                    / Math.Max(1, queueUrls.Count)));

            var attributes =
                await client.GetQueueAttributesAsync(
                    new GetQueueAttributesRequest
                    {
                        QueueUrl = url,
                        AttributeNames = new List<string>
                        {
                            "ApproximateNumberOfMessages",
                            "ApproximateNumberOfMessagesNotVisible"
                        }
                    });

            results.Add(
                new SqsQueueItem
                {
                    Name =
                        url.TrimEnd('/').Split('/').Last(),

                    Url = url,

                    AvailableMessages =
                        ParseIntAttribute(
                            attributes.Attributes,
                            "ApproximateNumberOfMessages"),

                    InFlightMessages =
                        ParseIntAttribute(
                            attributes.Attributes,
                            "ApproximateNumberOfMessagesNotVisible")
                });
        }

        Logger.Info($"SqsService: {results.Count} queue(s) loaded (profile '{profile}').");

        return results;
    }

    private int
    ParseIntAttribute(
        IDictionary<string, string>? attributes,
        string key)
    {
        return attributes != null
            && attributes.TryGetValue(key, out var value)
            && int.TryParse(value, out var parsed)
            ? parsed
            : 0;
    }

    public async Task<List<SqsMessageItem>>
    PeekMessages(
        string profile,
        string queueUrl,
        string? searchText,
        int maxMessages,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug(
            $"SqsService: searching '{queueUrl}' (query '{searchText}', max {maxMessages}).");

        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonSQSClient(
                credentials,
                RegionEndpoint.EUWest3);

        var found =
            new Dictionary<string, SqsMessageItem>();

        int emptyRoundsInARow = 0;

        const int MaxRounds = 20;
        const int VisibilityTimeoutSeconds = 2;

        for (int round = 0;
             round < MaxRounds
                && found.Count < maxMessages
                && emptyRoundsInARow < 2;
             round++)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            progress?.Report(
                (int)(found.Count * 100.0
                    / Math.Max(1, maxMessages)));

            var response =
                await client.ReceiveMessageAsync(
                    new ReceiveMessageRequest
                    {
                        QueueUrl = queueUrl,
                        MaxNumberOfMessages = 10,
                        VisibilityTimeout = VisibilityTimeoutSeconds,
                        WaitTimeSeconds = 0,
                        MessageSystemAttributeNames = new List<string>
                        {
                            "SentTimestamp"
                        }
                    },
                    cancellationToken);

            if (response.Messages == null
                || response.Messages.Count == 0)
            {
                emptyRoundsInARow++;
                continue;
            }

            emptyRoundsInARow = 0;

            foreach (var message in response.Messages)
            {
                if (found.ContainsKey(message.MessageId))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(searchText)
                    && message.Body.IndexOf(
                        searchText,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                found[message.MessageId] =
                    new SqsMessageItem
                    {
                        MessageId = message.MessageId,
                        SentTimestamp = FormatSentTimestamp(message),
                        Body = message.Body
                    };
            }
        }

        progress?.Report(100);

        Logger.Info(
            $"SqsService: {found.Count} message(s) found in '{queueUrl}'.");

        return found.Values
            .OrderByDescending(x => x.SentTimestamp)
            .Take(maxMessages)
            .ToList();
    }

    private string
    FormatSentTimestamp(
        Message message)
    {
        if (message.Attributes != null
            && message.Attributes.TryGetValue(
                "SentTimestamp",
                out var raw)
            && long.TryParse(raw, out var epochMs))
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds(epochMs)
                .LocalDateTime
                .ToString("yyyy-MM-dd HH:mm:ss");
        }

        return "";
    }
}
