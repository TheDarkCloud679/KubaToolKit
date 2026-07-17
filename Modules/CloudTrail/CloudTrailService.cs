using Amazon;
using Amazon.CloudTrail;
using Amazon.CloudTrail.Model;
using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.CloudTrail.Models;
using KubaToolKit.Shared.Services;

namespace KubaToolKit.Modules.CloudTrail;

public class CloudTrailService
{
    private const int MaxPages = 40;

    public async Task<(List<CloudTrailEventItem> Events, bool Truncated)>
    SearchEvents(
        string profile,
        string attributeKey,
        string attributeValue,
        DateTime? startDate,
        string startTime,
        DateTime? endDate,
        string endTime,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug(
            $"CloudTrailService: search attribute='{attributeKey}' value='{attributeValue}' (profile '{profile}').");

        var chain = new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(profile, out var credentials))
        {
            Logger.Error($"CloudTrailService: AWS profile not found '{profile}'.");

            throw new Exception($"AWS profile not found: {profile}");
        }

        using var client =
            new AmazonCloudTrailClient(
                credentials,
                RegionEndpoint.EUWest3);

        var (startUtc, endUtc) =
            BuildTimeRange(startDate, startTime, endDate, endTime);

        var request = new LookupEventsRequest
        {
            StartTime = startUtc,
            EndTime = endUtc
        };

        if (!string.IsNullOrWhiteSpace(attributeKey)
            && !string.IsNullOrWhiteSpace(attributeValue))
        {
            request.LookupAttributes = new List<LookupAttribute>
            {
                new LookupAttribute
                {
                    AttributeKey = attributeKey,
                    AttributeValue = attributeValue
                }
            };
        }

        var results = new List<CloudTrailEventItem>();

        int page = 0;
        bool truncated = false;

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response =
                    await client.LookupEventsAsync(request, cancellationToken);

                page++;

                foreach (var ev in response.Events ?? new List<Event>())
                {
                    results.Add(
                        new CloudTrailEventItem
                        {
                            Timestamp =
                                ev.EventTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                                ?? "",

                            EventName = ev.EventName ?? "",
                            Username = ev.Username ?? "",
                            EventSource = ev.EventSource ?? "unknown",

                            Resources =
                                string.Join(
                                    ", ",
                                    (ev.Resources ?? new List<Resource>())
                                        .Select(r =>
                                            string.IsNullOrWhiteSpace(r.ResourceType)
                                                ? r.ResourceName
                                                : $"{r.ResourceType}: {r.ResourceName}")),

                            CloudTrailEventJson = ev.CloudTrailEvent ?? ""
                        });
                }

                request.NextToken = response.NextToken;

                progress?.Report(results.Count);

                if (page >= MaxPages && !string.IsNullOrEmpty(request.NextToken))
                {
                    truncated = true;

                    Logger.Debug($"CloudTrailService: search truncated after {page} page(s).");

                    break;
                }
            }
            while (!string.IsNullOrEmpty(request.NextToken));
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("CloudTrailService: search cancelled.");

            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("CloudTrailService: search failed.", ex);

            throw;
        }

        Logger.Info(
            $"CloudTrailService: search completed, {results.Count} event(s) over {page} page(s).");

        return (
            results.OrderByDescending(x => x.Timestamp).ToList(),
            truncated);
    }

    private (DateTime StartUtc, DateTime EndUtc)
    BuildTimeRange(
        DateTime? startDate,
        string startTime,
        DateTime? endDate,
        string endTime)
    {
        var start = startDate!.Value.Date + TimeSpan.Parse(startTime);
        var end = endDate!.Value.Date + TimeSpan.Parse(endTime);

        var startUtc = start.ToUniversalTime();
        var endUtc = end.ToUniversalTime();

        var now = DateTime.UtcNow;

        if (endUtc > now)
        {
            endUtc = now;
        }

        if (startUtc > endUtc)
        {
            startUtc = endUtc;
        }

        return (startUtc, endUtc);
    }
}
