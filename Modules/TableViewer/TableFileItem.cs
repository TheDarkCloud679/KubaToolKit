namespace KubaToolKit.Modules.TableViewer;

// One row in the table-files list: a full path plus whether it's
// currently included when Decode runs. Checked by default so loading a
// whole folder is "decode everything, then uncheck what you don't want"
// rather than starting from nothing.
public class TableFileItem
{
    public required string Path { get; init; }

    public bool IsChecked { get; set; } = true;

    public string FileName =>
        System.IO.Path.GetFileName(Path);
}
