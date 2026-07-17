namespace KubaToolKit.Modules.ProjectInfo.Models;

public class ProjectInfoRoot
{
    public List<ProjectInfoProject> Projects { get; set; } = new();

    public Dictionary<string, string> ProfileProjectKeys { get; set; } = new();
}
