using System.Collections.ObjectModel;
namespace KubaToolKit.Modules.CloudWatchLogs.Models;

public class LogGroupResult
{
    public string
        LogGroup
    {
        get;
        set;
    } = "";

    public int
        Count
    {
        get;
        set;
    }

    public ObservableCollection<LogEntry>
        Logs
    {
        get;
        set;
    } = new();
}