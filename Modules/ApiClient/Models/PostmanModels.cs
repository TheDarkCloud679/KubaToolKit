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

    [JsonPropertyName("auth")]
    public PostmanAuth? Auth { get; set; }
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

    // Postman autorise aussi un auth directement sur un dossier (hérité
    // par ses enfants), en plus de celui sur la collection ou la requête.
    [JsonPropertyName("auth")]
    public PostmanAuth? Auth { get; set; }
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

    // Champs propres à KubaToolKit (absents des exports Postman standards,
    // ignorés sans risque si le fichier est réimporté dans Postman).
    [JsonPropertyName("_kubatoolkit_favorite")]
    public bool? Favorite { get; set; }

    [JsonPropertyName("_kubatoolkit_extract")]
    public List<PostmanHeader>? Extract { get; set; }

    [JsonPropertyName("auth")]
    public PostmanAuth? Auth { get; set; }
}

// Absent = hérite du parent (comportement par défaut de Postman).
// {"type":"noauth"} = explicitement aucune auth (n'hérite pas).
public class PostmanAuth
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("bearer")]
    public List<PostmanAuthAttribute>? Bearer { get; set; }

    [JsonPropertyName("basic")]
    public List<PostmanAuthAttribute>? Basic { get; set; }

    [JsonPropertyName("apikey")]
    public List<PostmanAuthAttribute>? ApiKey { get; set; }
}

public class PostmanAuthAttribute
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
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

    [JsonPropertyName("urlencoded")]
    public List<PostmanFormParam>? UrlEncoded { get; set; }

    [JsonPropertyName("formdata")]
    public List<PostmanFormParam>? FormData { get; set; }
}

public class PostmanFormParam
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
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
