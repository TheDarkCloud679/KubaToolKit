namespace KubaToolKit.Modules.ApiClient.Models;

/// Décrit le body d'une requête à la manière de Postman : un seul mode est
/// actif à la fois ("none", "formdata", "urlencoded", "raw", "binary" ou
/// "graphql"), les autres champs sont ignorés à l'envoi.
public class RequestBody
{
    public string Mode { get; set; } = "raw";
    public string? Raw { get; set; }
    public string? RawContentType { get; set; }
    public List<HeaderItem> FormData { get; set; } = new();
    public List<HeaderItem> UrlEncoded { get; set; } = new();
    public string? BinaryFilePath { get; set; }
    public string? GraphQlQuery { get; set; }
    public string? GraphQlVariables { get; set; }
}
