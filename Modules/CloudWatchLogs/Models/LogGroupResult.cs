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

    // Checked via the group header's own checkbox -- when at least one
    // group is selected, the bulk download buttons above the results
    // limit themselves to the selected group(s) instead of all of them.
    public bool
        IsSelected
    {
        get;
        set;
    }
}