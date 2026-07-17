namespace KubaToolKit.Modules.ProjectInfo.Models;

public class ProjectInfoProject
{
    public string Key { get; set; } = "";

    public List<ProjectInfoSection> Sections { get; set; } = new();
}
