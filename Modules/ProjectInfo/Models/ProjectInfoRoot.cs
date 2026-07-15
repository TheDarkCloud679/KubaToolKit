namespace KubaToolKit.Modules.ProjectInfo.Models;

/// Racine du fichier partagé Config/project-info.json : tous les projets
/// (un par profil AWS) dans un seul fichier, pour rester simple à
/// partager/versionner entre collègues.
public class ProjectInfoRoot
{
    public List<ProjectInfoProject> Projects { get; set; } = new();
}
