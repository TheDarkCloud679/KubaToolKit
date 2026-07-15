using Amazon;
using Amazon.CloudTrail;
using Amazon.CloudTrail.Model;
using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.CloudTrail.Models;

namespace KubaToolKit.Modules.CloudTrail;

public class CloudTrailService
{
    // LookupEvents est limitée à ~2 requêtes/seconde par compte et 50
    // évènements par page : sans filtre ("All events"), une recherche sur
    // un compte actif peut porter sur des milliers d'évènements et tourner
    // pendant plusieurs minutes. On plafonne pour garantir un résultat en
    // temps raisonnable plutôt que de paraître figé indéfiniment.
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

        // Total inconnu à l'avance (l'API ne renvoie qu'une page à la
        // fois) : on rapporte le nombre d'évènements trouvés jusqu'ici
        // plutôt qu'un pourcentage, la barre elle-même reste indéterminée
        // côté vue.
        int page = 0;
        bool truncated = false;

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
                break;
            }
        }
        while (!string.IsNullOrEmpty(request.NextToken));

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

        return (start.ToUniversalTime(), end.ToUniversalTime());
    }
}
