namespace KubaToolKit.Modules.ApiClient.Models;

public enum AuthType
{
    None,
    Bearer,
    Basic,
    ApiKey
}

public class AuthConfig
{
    public AuthType Type { get; set; } = AuthType.None;
    public string BearerToken { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string ApiKeyName { get; set; } = "";
    public string ApiKeyValue { get; set; } = "";
}
