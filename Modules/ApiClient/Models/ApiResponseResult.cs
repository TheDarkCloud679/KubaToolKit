using KubaToolKit.Shared.Services;
using System.Windows.Media;

namespace KubaToolKit.Modules.ApiClient.Models;

public class ApiResponseResult
{
    public int StatusCode { get; set; }
    public string ReasonPhrase { get; set; } = "";
    public long ElapsedMs { get; set; }
    public string Headers { get; set; } = "";
    public string Body { get; set; } = "";

    public string StatusDisplay =>
        $"{StatusCode} {ReasonPhrase}".Trim();

    public Brush? StatusBackground =>
        MetricColorHelper.GetHttpStatusBrush(StatusCode);
}
