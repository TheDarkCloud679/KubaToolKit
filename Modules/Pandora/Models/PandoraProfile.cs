namespace KubaToolKit.Modules.Pandora.Models;

/// Un site Pandora FMS, lu depuis le fichier local gitignored
/// Config/pandora-profiles.json -- voir Config/pandora-profiles.example.json
/// pour le format attendu. La console est derrière une SSO OAuth2 (le
/// cookie de session vient de PandoraLoginWindow, pas de ce fichier), mais
/// api.php a sa propre couche d'authentification indépendante de cette
/// session -- User/Pass/ApiPassword restent donc nécessaires en plus du
/// cookie si le serveur l'exige.
public class PandoraProfile
{
    public string Name { get; set; } = "";

    public string Url { get; set; } = "";

    public string User { get; set; } = "";

    public string Pass { get; set; } = "";

    public string ApiPassword { get; set; } = "";

    public override string ToString() => Name;
}
