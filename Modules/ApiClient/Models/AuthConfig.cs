namespace KubaToolKit.Modules.ApiClient.Models;

public enum AuthType
{
    Inherit,
    None,
    Bearer,
    Basic,
    ApiKey
}

public class AuthConfig
{
    // Inherit par défaut (comme Postman) : un nœud fraîchement créé sans
    // auth explicite (ex. "Nouveau dossier") doit hériter du parent, pas
    // se comporter comme s'il avait explicitement choisi "No Auth".
    public AuthType Type { get; set; } = AuthType.Inherit;
    public string BearerToken { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string ApiKeyName { get; set; } = "";
    public string ApiKeyValue { get; set; } = "";
}
