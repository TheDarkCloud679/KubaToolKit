using KubaToolKit.Modules.TableViewer.Decoding;

namespace KubaToolKit.Modules.TableViewer;

// One row in the results index -- one per decoded file, so its fields
// can be shown on demand instead of merging every file into one grid.
public class FileDecodeResultItem
{
    public required string FileName { get; init; }

    public required TableDecodeResult Result { get; init; }

    public string StatusText =>
        Result.Success ? $"{Result.Trigram} - {Result.Fields.Count} value(s)" : Result.ErrorMessage ?? "Failed";
}
