using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.RDS;
using Amazon.RDS.Model;
using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.Dashboard.Models;
using KubaToolKit.Shared.Services;

namespace KubaToolKit.Modules.Dashboard;

public class DashboardService
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

    public async Task<List<RdsMetricItem>>
    GetRdsMetrics(
        string profile,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug($"DashboardService: chargement des instances RDS (profil '{profile}').");

        var credentials =
            GetCredentials(profile);

        using var rdsClient =
            new AmazonRDSClient(
                credentials,
                RegionEndpoint.EUWest3);

        using var cloudWatchClient =
            new AmazonCloudWatchClient(
                credentials,
                RegionEndpoint.EUWest3);

        var instances =
            new List<DBInstance>();

        string? marker = null;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var response =
                await rdsClient.DescribeDBInstancesAsync(
                    new DescribeDBInstancesRequest
                    {
                        Marker = marker
                    });

            if (response.DBInstances != null)
            {
                instances.AddRange(
                    response.DBInstances);
            }

            marker = response.Marker;
        }
        while (!string.IsNullOrEmpty(marker));

        var results =
            new List<RdsMetricItem>();

        int processed = 0;

        foreach (var instance
                 in instances.OrderBy(x => x.DBInstanceIdentifier))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            processed++;

            progress?.Report(
                (int)(processed * 100.0
                    / Math.Max(1, instances.Count)));

            var cpu =
                await GetLatestMetric(
                    cloudWatchClient,
                    instance.DBInstanceIdentifier,
                    "CPUUtilization",
                    cancellationToken);

            var connections =
                await GetLatestMetric(
                    cloudWatchClient,
                    instance.DBInstanceIdentifier,
                    "DatabaseConnections",
                    cancellationToken);

            var tags =
                instance.TagList
                ?? new List<Amazon.RDS.Model.Tag>();

            results.Add(
                new RdsMetricItem
                {
                    Identifier = instance.DBInstanceIdentifier,
                    Engine = instance.Engine,
                    Status = instance.DBInstanceStatus,
                    CpuPercent = cpu,
                    DatabaseConnections = connections,

                    AutoStart =
                        FindTagValue(
                            tags,
                            "auto-start",
                            "autostart",
                            "auto_start")
                        ?? "—",

                    AutoStop =
                        FindTagValue(
                            tags,
                            "auto-stop",
                            "autostop",
                            "auto_stop")
                        ?? "—"
                });
        }

        Logger.Info($"DashboardService: {results.Count} instance(s) RDS chargée(s).");

        return results;
    }

    private async Task<double?>
    GetLatestMetric(
        AmazonCloudWatchClient client,
        string dbInstanceIdentifier,
        string metricName,
        CancellationToken cancellationToken)
    {
        var now =
            DateTime.UtcNow;

        var response =
            await client.GetMetricStatisticsAsync(
                new GetMetricStatisticsRequest
                {
                    Namespace = "AWS/RDS",
                    MetricName = metricName,
                    Dimensions = new List<Dimension>
                    {
                        new Dimension
                        {
                            Name = "DBInstanceIdentifier",
                            Value = dbInstanceIdentifier
                        }
                    },
                    StartTime = now.AddMinutes(-15),
                    EndTime = now,
                    Period = 300,
                    Statistics = new List<string> { "Average" }
                },
                cancellationToken);

        if (response.Datapoints == null
            || response.Datapoints.Count == 0)
        {
            return null;
        }

        return response.Datapoints
            .OrderByDescending(x => x.Timestamp)
            .First()
            .Average;
    }

    /// Historique d'une métrique CloudWatch (namespace/dimensions
    /// génériques) pour afficher un graphique, sur une plage explicite
    /// (UTC) plutôt qu'implicitement "maintenant moins une durée" : la
    /// fenêtre affichée dans MetricChartWindow est réglable par
    /// l'utilisateur, comme la recherche de logs. La période demandée
    /// vise ~1 point par minute sur la plage.
    public async Task<List<(DateTime Timestamp, double Value)>>
    GetMetricHistory(
        string profile,
        string @namespace,
        string metricName,
        List<Dimension> dimensions,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        CancellationToken cancellationToken = default)
    {
        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonCloudWatchClient(
                credentials,
                RegionEndpoint.EUWest3);

        var response =
            await client.GetMetricStatisticsAsync(
                new GetMetricStatisticsRequest
                {
                    Namespace = @namespace,
                    MetricName = metricName,
                    Dimensions = dimensions,
                    StartTime = startTimeUtc,
                    EndTime = endTimeUtc,
                    Period = ComputePeriodSeconds(endTimeUtc - startTimeUtc),
                    Statistics = new List<string> { "Average" }
                },
                cancellationToken);

        if (response.Datapoints == null)
        {
            return new List<(DateTime, double)>();
        }

        return response.Datapoints
            .Where(x => x.Timestamp.HasValue && x.Average.HasValue)
            .OrderBy(x => x.Timestamp!.Value)
            .Select(x => (x.Timestamp!.Value, x.Average!.Value))
            .ToList();
    }

    public async Task<List<Ec2MetricItem>>
    GetEc2Instances(
        string profile,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug($"DashboardService: chargement des instances EC2 (profil '{profile}').");

        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonEC2Client(
                credentials,
                RegionEndpoint.EUWest3);

        var items =
            new List<Ec2MetricItem>();

        string? nextToken = null;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var response =
                await client.DescribeInstancesAsync(
                    new DescribeInstancesRequest
                    {
                        NextToken = nextToken
                    },
                    cancellationToken);

            if (response.Reservations != null)
            {
                foreach (var reservation in response.Reservations)
                {
                    if (reservation.Instances == null)
                    {
                        continue;
                    }

                    foreach (var instance in reservation.Instances)
                    {
                        var stateName =
                            instance.State?.Name?.Value
                            ?? "";

                        // Une instance terminée disparaît définitivement :
                        // pas utile dans un dashboard d'instances actives.
                        if (stateName == "terminated")
                        {
                            continue;
                        }

                        var tags =
                            instance.Tags
                            ?? new List<Amazon.EC2.Model.Tag>();

                        items.Add(
                            new Ec2MetricItem
                            {
                                InstanceId = instance.InstanceId,

                                Name =
                                    FindTagValue(tags, "Name")
                                    ?? instance.InstanceId,

                                InstanceType =
                                    instance.InstanceType?.Value
                                    ?? "",

                                State = stateName,

                                AutoStart =
                                    FindTagValue(
                                        tags,
                                        "auto-start",
                                        "autostart",
                                        "auto_start")
                                    ?? "—",

                                AutoStop =
                                    FindTagValue(
                                        tags,
                                        "auto-stop",
                                        "autostop",
                                        "auto_stop")
                                    ?? "—"
                            });
                    }
                }
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        Logger.Info($"DashboardService: {items.Count} instance(s) EC2 chargée(s).");

        return items
            .OrderBy(x => x.Name)
            .ToList();
    }

    private string?
    FindTagValue(
        List<Amazon.EC2.Model.Tag> tags,
        params string[] keyVariants)
    {
        foreach (var variant in keyVariants)
        {
            var match =
                tags.FirstOrDefault(t =>
                    string.Equals(
                        t.Key,
                        variant,
                        StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match.Value;
            }
        }

        return null;
    }

    private string?
    FindTagValue(
        List<Amazon.RDS.Model.Tag> tags,
        params string[] keyVariants)
    {
        foreach (var variant in keyVariants)
        {
            var match =
                tags.FirstOrDefault(t =>
                    string.Equals(
                        t.Key,
                        variant,
                        StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match.Value;
            }
        }

        return null;
    }

    private int
    ComputePeriodSeconds(
        TimeSpan duration)
    {
        // CloudWatch exige un multiple de 60s ; on vise ~60 points sur la
        // plage demandée (1 point/minute pour une fenêtre de 1h).
        const int TargetPoints = 60;

        double rawSeconds =
            duration.TotalSeconds / TargetPoints;

        int period =
            (int)(Math.Ceiling(rawSeconds / 60.0) * 60);

        return Math.Max(60, period);
    }
}
