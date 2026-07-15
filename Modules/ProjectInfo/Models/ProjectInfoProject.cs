namespace KubaToolKit.Modules.ProjectInfo.Models;

public class ProjectInfoProject
{
    /// Nom de projet choisi par l'utilisateur (ex: "ClientA"), pas
    /// forcément un profil AWS exact -- plusieurs profils (prod/preprod/
    /// test) peuvent pointer vers la même clé pour partager ces données,
    /// via ProjectInfoRoot.ProfileProjectKeys.
    public string Key { get; set; } = "";

    public List<ProjectInfoSection> Sections { get; set; } = new();
}
