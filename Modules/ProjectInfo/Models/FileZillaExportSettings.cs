namespace KubaToolKit.Modules.ProjectInfo.Models;

// Remembered per section so re-exporting after editing rows doesn't
// require re-entering the shared login/port/key file every time.
public class FileZillaExportSettings
{
    public string NameColumn { get; set; } = "";

    public string HostColumn { get; set; } = "";

    public string Username { get; set; } = "";

    public string Port { get; set; } = "22";

    public string KeyFilePath { get; set; } = "";

    public string FolderName { get; set; } = "";
}
