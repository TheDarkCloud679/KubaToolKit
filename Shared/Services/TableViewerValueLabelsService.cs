using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KubaToolKit.Shared.Services;

// Same file format and role as API Client's ValueLabels.json: maps a
// decoded field's exact name to a label per raw value, shown in Table
// Viewer's Cards view as "Label (value)" instead of the bare value.
public class TableViewerValueLabelsService
{
    public static string ValueLabelsFile =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "table-viewer-value-labels.json");

    public string? LastError { get; private set; }

    public Dictionary<string, Dictionary<string, string>>
    Load()
    {
        LastError = null;

        var filePath = ValueLabelsFile;
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            try
            {
                File.WriteAllText(
                    filePath,
                    """
                    {
                      "_comment": "Maps a decoded field's exact name (as shown in the grid) to a label for its raw value, shown in Cards view as 'Label (value)'. One entry per field name, with a label per observed value. This _comment key is ignored.",
                      "Type (piece ou billets)": {
                        "0": "Coin"
                      }
                    }
                    """);
            }
            catch
            {
            }
        }

        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(filePath);

            var documentOptions =
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

            if (JsonNode.Parse(json, documentOptions: documentOptions) is not JsonObject root)
            {
                LastError = "The file does not contain a valid JSON object.";

                return result;
            }

            foreach (var (field, node) in root)
            {
                if (node is not JsonObject codes)
                {
                    continue;
                }

                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (code, value) in codes)
                {
                    if (value != null)
                    {
                        labels[code] = value.ToString();
                    }
                }

                result[field] = labels;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;

            Logger.Error("TableViewerValueLabelsService: failed to load table-viewer-value-labels.json.", ex);
        }

        return result;
    }
}
