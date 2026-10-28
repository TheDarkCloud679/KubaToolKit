using KubaToolKit.Shared.Services;
using System.IO;

namespace KubaToolKit.Modules.TableViewer.Decoding;

// Resolves a binary table file to its definition and decodes it -- the
// same two-step lookup QTable itself does: the file's own name's first 3
// letters are its "trigramme" (e.g. ADV00004.D010 -> table "ADV"), looked
// up across whichever of the 3 definitions files (tables/stats/dumps,
// matching QTable's own Parameters dialog) are configured.
public static class TableFileDecoder
{
    public static TableDecodeResult
    Decode(
        string filePath,
        TableViewerSettings settings)
    {
        var trigram = GetTrigram(filePath);

        var definitionsFiles =
            new[]
            {
                settings.TablesDefinitionsXmlPath,
                settings.StatsDefinitionsXmlPath,
                settings.DumpsDefinitionsXmlPath
            };

        if (definitionsFiles.All(string.IsNullOrWhiteSpace))
        {
            return TableDecodeResult.Failure(
                "No definitions file is configured. Set it in the settings (gear icon).");
        }

        foreach (var definitionsFile in definitionsFiles)
        {
            if (string.IsNullOrWhiteSpace(definitionsFile))
            {
                continue;
            }

            var table = TableXmlLoader.FindTable(definitionsFile, trigram);

            if (table == null)
            {
                continue;
            }

            byte[] data;

            try
            {
                data = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return TableDecodeResult.Failure($"Couldn't read the file: {ex.Message}");
            }

            var interpreter = new TableInterpreter(data);
            var (fields, notes) = interpreter.Run(table);

            return
                new TableDecodeResult
                {
                    Success = true,
                    Trigram = trigram,
                    DefinitionsFile = definitionsFile,
                    Fields = fields.ToList(),
                    Notes = notes.ToList()
                };
        }

        return TableDecodeResult.Failure(
            $"No table definition named '{trigram}' was found in the configured definitions files.");
    }

    private static string
    GetTrigram(
        string filePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(filePath);

        return (baseName.Length >= 3 ? baseName[..3] : baseName).ToUpperInvariant();
    }
}
