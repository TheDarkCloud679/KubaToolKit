namespace KubaToolKit.Modules.ProjectInfo.Models;

public class ProjectInfoSection
{
    public string Name { get; set; } = "";

    public List<string> Columns { get; set; } = new();

    public List<Dictionary<string, string>> Rows { get; set; } = new();

    public FileZillaExportSettings? FileZillaExport { get; set; }
}
