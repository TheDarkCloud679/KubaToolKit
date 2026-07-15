namespace KubaToolKit.Modules.Pandora.Models;

/// Un site Pandora FMS (nom + URL), lu depuis le fichier local gitignored
/// Config/pandora-profiles.json -- voir Config/pandora-profiles.example.json
/// pour le format attendu. Pas d'identifiants ici : la console est
/// derrière une SSO OAuth2, l'authentification passe par une fenêtre de
/// connexion (PandoraLoginWindow) plutôt que par un couple user/pass
/// stocké sur disque.
public class PandoraProfile
{
    public string Name { get; set; } = "";

    public string Url { get; set; } = "";

    public override string ToString() => Name;
}
