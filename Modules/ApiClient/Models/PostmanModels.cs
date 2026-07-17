using System.Text.Json;
using System.Text.Json.Serialization;

namespace KubaToolKit.Modules.ApiClient.Models;

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

    [JsonPropertyName("item")]
    public List<PostmanItem>? Item { get; set; }

    [JsonPropertyName("request")]
    public PostmanRequest? Request { get; set; }

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

    [JsonPropertyName("url")]
    public JsonElement Url { get; set; }

    [JsonPropertyName("_kubatoolkit_favorite")]
    public bool? Favorite { get; set; }

    [JsonPropertyName("_kubatoolkit_extract")]
    public List<PostmanHeader>? Extract { get; set; }

    [JsonPropertyName("auth")]
    public PostmanAuth? Auth { get; set; }
}

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
