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
        Logger.Debug($"DashboardService: loading RDS instances (profile '{profile}').");

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

            var dimensions = new List<Dimension>
            {
                new Dimension
                {
                    Name = "DBInstanceIdentifier",
                    Value = instance.DBInstanceIdentifier
                }
            };

            var cpu =
                await GetLatestMetricValue(
                    cloudWatchClient,
                    "AWS/RDS",
                    "CPUUtilization",
                    dimensions,
                    cancellationToken);

            var connections =
                await GetLatestMetricValue(
                    cloudWatchClient,
                    "AWS/RDS",
                    "DatabaseConnections",
                    dimensions,
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

        Logger.Info($"DashboardService: {results.Count} RDS instance(s) loaded.");

        return results;
    }

    private async Task<double?>
    GetLatestMetricValue(
        AmazonCloudWatchClient client,
        string @namespace,
        string metricName,
        List<Dimension> dimensions,
        CancellationToken cancellationToken)
    {
        var now =
            DateTime.UtcNow;

        var response =
            await client.GetMetricStatisticsAsync(
                new GetMetricStatisticsRequest
                {
                    Namespace = @namespace,
                    MetricName = metricName,
                    Dimensions = dimensions,
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
        Logger.Debug($"DashboardService: loading EC2 instances (profile '{profile}').");

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

        Logger.Info($"DashboardService: {items.Count} EC2 instance(s) loaded.");

        return items
            .OrderBy(x => x.Name)
            .ToList();
    }

    /// One row per instance x mount point: disk_used_percent isn't
    /// published with a fixed dimension set (it depends on how the
    /// CloudWatch agent on each box is configured), so the actual
    /// dimensions are discovered per instance via ListMetrics instead of
    /// assumed up front, then each matching time series is read
    /// individually.
    public async Task<List<Ec2DiskUsage>>
    GetEc2DiskUsage(
        string profile,
        IReadOnlyList<Ec2MetricItem> instances,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Logger.Debug($"DashboardService: scanning disk usage for {instances.Count} EC2 instance(s) (profile '{profile}').");

        var credentials =
            GetCredentials(profile);

        using var client =
            new AmazonCloudWatchClient(
                credentials,
                RegionEndpoint.EUWest3);

        var instanceNamesById =
            instances.ToDictionary(
                x => x.InstanceId,
                x => x.Name,
                StringComparer.OrdinalIgnoreCase);

        var metrics =
            new List<Amazon.CloudWatch.Model.Metric>();

        string? nextToken = null;

        do
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var response =
                await client.ListMetricsAsync(
                    new ListMetricsRequest
                    {
                        Namespace = "CWAgent",
                        MetricName = "disk_used_percent",
                        NextToken = nextToken
                    },
                    cancellationToken);

            if (response.Metrics != null)
            {
                metrics.AddRange(response.Metrics);
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        var relevant =
            metrics
                .Where(m =>
                    m.Dimensions != null
                    && m.Dimensions.Any(d =>
                        string.Equals(d.Name, "InstanceId", StringComparison.OrdinalIgnoreCase)
                        && instanceNamesById.ContainsKey(d.Value)))
                .ToList();

        var results =
            new List<Ec2DiskUsage>();

        int processed = 0;

        foreach (var metric in relevant)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            processed++;

            progress?.Report(
                (int)(processed * 100.0
                    / Math.Max(1, relevant.Count)));

            var instanceId =
                metric.Dimensions
                    .First(d => string.Equals(d.Name, "InstanceId", StringComparison.OrdinalIgnoreCase))
                    .Value;

            var mountPath =
                metric.Dimensions
                    .FirstOrDefault(d => string.Equals(d.Name, "path", StringComparison.OrdinalIgnoreCase))
                    ?.Value
                ?? "/";

            var usedPercent =
                await GetLatestMetricValue(
                    client,
                    "CWAgent",
                    "disk_used_percent",
                    metric.Dimensions,
                    cancellationToken);

            if (!usedPercent.HasValue)
            {
                continue;
            }

            results.Add(
                new Ec2DiskUsage
                {
                    InstanceId = instanceId,
                    InstanceName = instanceNamesById.GetValueOrDefault(instanceId, instanceId),
                    MountPath = mountPath,
                    UsedPercent = usedPercent.Value
                });
        }

        Logger.Info($"DashboardService: disk usage scan found {results.Count} mount point(s) across {relevant.Select(m => m.Dimensions.First(d => d.Name == "InstanceId").Value).Distinct().Count()} instance(s).");

        return results
            .OrderBy(x => x.InstanceName)
            .ThenBy(x => x.MountPath)
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
        const int TargetPoints = 60;

        double rawSeconds =
            duration.TotalSeconds / TargetPoints;

        int period =
            (int)(Math.Ceiling(rawSeconds / 60.0) * 60);

        return Math.Max(60, period);
    }
}
