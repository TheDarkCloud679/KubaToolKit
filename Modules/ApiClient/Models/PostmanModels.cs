using System.Text.Json;
using System.Text.Json.Serialization;

namespace KubaToolKit.Modules.ApiClient.Models;

// Sous-ensemble du schéma d'export Postman (collection v2.1 / environment)
// suffisant pour importer method/url/headers/body et des variables
// {{clé}} : pas besoin de supporter form-data, GraphQL, auth par requête,
// etc. pour une première version.

public class PostmanCollection
{
    [JsonPropertyName("info")]
    public PostmanInfo? Info { get; set; }

    [JsonPropertyName("item")]
    public List<PostmanItem>? Item { get; set; }
}

public class PostmanInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class PostmanItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // Présent pour un dossier.
    [JsonPropertyName("item")]
    public List<PostmanItem>? Item { get; set; }

    // Présent pour une requête (feuille).
    [JsonPropertyName("request")]
    public PostmanRequest? Request { get; set; }
}

public class PostmanRequest
{
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("header")]
    public List<PostmanHeader>? Header { get; set; }

    [JsonPropertyName("body")]
    public PostmanBody? Body { get; set; }

    // Postman exporte tantôt une simple string, tantôt un objet
    // {raw, host, path, query...} : JsonElement capture les deux formes.
    [JsonPropertyName("url")]
    public JsonElement Url { get; set; }
}

public class PostmanHeader
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
}

public class PostmanBody
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("raw")]
    public string? Raw { get; set; }
}

public class PostmanEnvironment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("values")]
    public List<PostmanEnvironmentValue>? Values { get; set; }
}

public class PostmanEnvironmentValue
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
