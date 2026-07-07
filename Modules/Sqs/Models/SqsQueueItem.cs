namespace KubaToolKit.Modules.Sqs.Models;

public class SqsQueueItem
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int AvailableMessages { get; set; }
    public int InFlightMessages { get; set; }
}
