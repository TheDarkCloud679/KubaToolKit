namespace KubaToolKit.Modules.Pandora.Models;

/// Un site Pandora FMS (URL + identifiants), lu depuis le fichier local
/// gitignored Config/pandora-profiles.json -- voir
/// Config/pandora-profiles.example.json pour le format attendu.
public class PandoraProfile
{
    public string Name { get; set; } = "";

    public string Url { get; set; } = "";

    public string User { get; set; } = "";

    public string Pass { get; set; } = "";

    /// Mot de passe API séparé (Setup > API côté console Pandora),
    /// optionnel selon la configuration du serveur.
    public string ApiPassword { get; set; } = "";

    public override string ToString() => Name;
}
