namespace KubaToolKit.Infrastructure;

/// Extension point: a new brick registers itself here (and is wired into the
/// Shell like the existing modules) instead of being folded into the Shell code.
public static class ToolModuleRegistry
{
    public static IReadOnlyList<IToolModule> CreateModules() =>
        new IToolModule[]
        {
            new Modules.Dashboard.DashboardModule(),
            new Modules.CloudWatchLogs.CloudWatchLogsModule(),
            new Modules.S3Explorer.S3ExplorerModule(),
            new Modules.Sqs.SqsModule(),
            new Modules.StepFunctions.StepFunctionsModule(),
            new Modules.ApiClient.ApiClientModule(),
        };
}
