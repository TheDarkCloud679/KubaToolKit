namespace KubaToolKit.Modules.S3Explorer.Models;

public class ArchiveEntryItem
{
    public string
        Name
    {
        get;
        set;
    } = "";

    public string
        FullPath
    {
        get;
        set;
    } = "";

    public bool
        IsDirectory
    {
        get;
        set;
    }

    public List<ArchiveEntryItem>
        Children
    {
        get;
        set;
    } =
        new();

    public string
        ArchivePath
    {
        get;
        set;
    } = "";

    public string
        EntryPath
    {
        get;
        set;
    } = "";
}