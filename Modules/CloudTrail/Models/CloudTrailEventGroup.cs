using System.Collections.ObjectModel;

namespace KubaToolKit.Modules.CloudTrail.Models;

public class CloudTrailEventGroup
{
    public string EventSource { get; set; } = "";

    public int Count { get; set; }

    public ObservableCollection<CloudTrailEventItem> Events { get; set; } = new();
}
