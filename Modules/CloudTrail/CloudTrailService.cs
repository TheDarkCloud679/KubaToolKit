using Amazon;
using Amazon.CloudTrail;
using Amazon.CloudTrail.Model;
using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.CloudTrail.Models;

namespace KubaToolKit.Modules.CloudTrail;

public class CloudTrailService
{
    public async Task<List<CloudTrailEventItem>>
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
        var chain = new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(profile, out var credentials))
        {
            throw new Exception($"Profil AWS introuvable : {profile}");
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

        // L'API ne renvoie qu'une page à la fois et ne donne pas de total :
        // on avance la barre par palier à chaque page reçue, sans jamais
        // atteindre 100% avant la fin réelle de la pagination.
        int page = 0;

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

            progress?.Report(Math.Min(90, page * 10));
        }
        while (!string.IsNullOrEmpty(request.NextToken));

        progress?.Report(100);

        return results
            .OrderByDescending(x => x.Timestamp)
            .ToList();
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

        return (start.ToUniversalTime(), end.ToUniversalTime());
    }
}
