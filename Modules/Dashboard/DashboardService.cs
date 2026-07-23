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

    // Metric names that represent disk space usage, seen across different
    // CloudWatch Agent builds/OSes/configs -- the classic JSON-based agent
    // publishes "disk_used_percent" (used %), while an OTel-based agent
    // (common for Windows, since it reads Performance Counters) is often
    // set up with a custom namespace and a renamed metric such as
    // "DiskFreeSpace%" (free %, needs inverting) instead. Anything with
    // "disk" and ("used"/"free"/"space") in the name is treated as a
    // candidate; "disk time"/"utilization" metrics are I/O activity, not
    // space, and are deliberately excluded by not matching that filter.
    private static bool
    LooksLikeDiskSpaceMetric(
        string metricName) =>
        metricName.Contains("disk", StringComparison.OrdinalIgnoreCase)
        && (metricName.Contains("used", StringComparison.OrdinalIgnoreCase)
            || metricName.Contains("free", StringComparison.OrdinalIgnoreCase)
            || metricName.Contains("space", StringComparison.OrdinalIgnoreCase));

    /// One row per instance x mount point. Disk space metrics aren't
    /// published under one fixed namespace/metric name/dimension set --
    /// it depends entirely on how each box's CloudWatch agent is
    /// configured (classic JSON agent vs. a customized OTel-based one,
    /// different namespaces, renamed metrics...) -- so instead of assuming
    /// one, every metric CloudWatch has for that instance is pulled and
    /// filtered by name.
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

        var results =
            new List<Ec2DiskUsage>();

        int processed = 0;

        foreach (var instance in instances)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            processed++;

            progress?.Report(
                (int)(processed * 100.0
                    / Math.Max(1, instances.Count)));

            var instanceMetrics =
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
                            // No Namespace/MetricName filter: this instance's
                            // metrics could be under any namespace, and the
                            // agent may have renamed the metric entirely.
                            Dimensions = new List<DimensionFilter>
                            {
                                new DimensionFilter
                                {
                                    Name = "InstanceId",
                                    Value = instance.InstanceId
                                }
                            },
                            NextToken = nextToken
                        },
                        cancellationToken);

                if (response.Metrics != null)
                {
                    instanceMetrics.AddRange(response.Metrics);
                }

                nextToken = response.NextToken;
            }
            while (!string.IsNullOrEmpty(nextToken));

            var diskMetrics =
                instanceMetrics
                    .Where(m => LooksLikeDiskSpaceMetric(m.MetricName))
                    .ToList();

            foreach (var metric in diskMetrics)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var value =
                    await GetLatestMetricValue(
                        client,
                        metric.Namespace,
                        metric.MetricName,
                        metric.Dimensions ?? new List<Dimension>(),
                        cancellationToken);

                if (!value.HasValue)
                {
                    continue;
                }

                var isFreeSpaceMetric =
                    metric.MetricName.Contains("free", StringComparison.OrdinalIgnoreCase);

                var usedPercent =
                    isFreeSpaceMetric
                        ? 100 - value.Value
                        : value.Value;

                // Whatever extra dimension the agent tagged the volume with
                // (a drive letter, a mount path...) identifies which disk
                // this is; when there isn't one, the metric's own name is
                // the next best label so two rows for the same instance
                // aren't indistinguishable.
                var mountLabel =
                    metric.Dimensions?
                        .FirstOrDefault(d => !string.Equals(d.Name, "InstanceId", StringComparison.OrdinalIgnoreCase))
                        ?.Value
                    ?? metric.MetricName;

                results.Add(
                    new Ec2DiskUsage
                    {
                        InstanceId = instance.InstanceId,
                        InstanceName = instance.Name,
                        MountPath = mountLabel,
                        UsedPercent = Math.Clamp(usedPercent, 0, 100)
                    });
            }
        }

        Logger.Info($"DashboardService: disk usage scan found {results.Count} mount point(s) across {instances.Count} instance(s) checked.");

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
