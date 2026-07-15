namespace KubaToolKit.Modules.ProjectInfo.Models;

/// Racine du fichier partagé Config/project-info.json : tous les projets
/// dans un seul fichier, pour rester simple à partager/versionner entre
/// collègues.
public class ProjectInfoRoot
{
    public List<ProjectInfoProject> Projects { get; set; } = new();

    /// Associe un profil AWS exact (ex: "ClientA-prod") à la clé de projet
    /// qu'il partage (ex: "ClientA") -- ainsi Prod/Preprod/Test d'un même
    /// projet peuvent pointer vers les mêmes données. Un profil sans
    /// entrée ici garde son propre nom comme clé (comportement par défaut,
    /// pas de partage tant que ce n'est pas explicitement demandé).
    public Dictionary<string, string> ProfileProjectKeys { get; set; } = new();
}
