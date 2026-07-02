namespace KubaToolKit.Modules.CloudWatchLogs.Models;

public class LogGroupCategory
{
    public string
        Name
    {
        get;
        set;
    } = "";

    public List<string>
        Patterns
    {
        get;
        set;
    } =
        new();
}