namespace KubaToolKit.Modules.S3Explorer.Models;

// Reported once per chunk while DownloadAsZip streams an S3 object
// straight into a zip entry -- download and compression happen in the
// same pass, so FilePercent already reflects both together.
public class ZipDownloadProgress
{
    public int FileIndex { get; set; }

    public int FileCount { get; set; }

    public string FileName { get; set; } = "";

    public int FilePercent { get; set; }
}
