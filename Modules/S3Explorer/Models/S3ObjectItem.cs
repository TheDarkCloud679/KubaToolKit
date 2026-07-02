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
}