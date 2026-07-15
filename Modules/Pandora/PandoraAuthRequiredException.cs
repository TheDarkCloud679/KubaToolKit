namespace KubaToolKit.Modules.Pandora;

/// Levée quand une requête vers l'API Pandora n'a pas (ou plus) de session
/// valide -- soit aucun login n'a encore été fait pour ce profil, soit la
/// session a expiré et le serveur redirige de nouveau vers la SSO. La vue
/// appelante réagit en ouvrant PandoraLoginWindow puis en réessayant une
/// fois, à l'identique de AwsSsoService.IsSsoExpired côté AWS.
public class PandoraAuthRequiredException : Exception
{
    public PandoraAuthRequiredException()
        : base("Authentification Pandora requise.")
    {
    }
}
