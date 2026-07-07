namespace KubaToolKit.Modules.Sqs.Models;

public class SqsMessageItem
{
    public string MessageId { get; set; } = "";
    public string SentTimestamp { get; set; } = "";
    public string Body { get; set; } = "";
}
