using System.IO;

namespace KubaToolKit.Modules.S3Explorer.Models;

public class S3ObjectItem
{
    public string
        Name
    {
        get;
        set;
    } = "";

    public string
        Key
    {
        get;
        set;
    } = "";

    public long
        Size
    {
        get;
        set;
    }

    public DateTime
        LastModified
    {
        get;
        set;
    }

    public string
        SizeDisplay
    {
        get
        {
            if (Size
                < 1024)
            {
                return
                    $"{Size} B";
            }

            if (Size
                < 1024 * 1024)
            {
                return
                    $"{Size / 1024d:0.0} KB";
            }

            return
                $"{Size / 1024d / 1024d:0.0} MB";
        }
    }

    // Drives which file icon the results grid shows -- coarse enough that
    // new extensions just fall into "Generic" instead of needing a new case.
    public string
        IconKind
    {
        get
        {
            var extension =
                Path.GetExtension(Name).ToLowerInvariant();

            if (extension is ".zip" or ".7z" or ".rar" or ".tar" or ".gz")
            {
                return "Archive";
            }

            if (extension is ".md" or ".txt" or ".json" or ".xml" or ".yml" or ".yaml" or ".csv" or ".log" or ".ini" or ".conf")
            {
                return "Doc";
            }

            return "Generic";
        }
    }
}