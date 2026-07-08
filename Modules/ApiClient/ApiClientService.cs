using KubaToolKit.Modules.ApiClient.Models;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KubaToolKit.Modules.ApiClient;

public class ApiClientService
{
    // Instance HttpClient partagée et réutilisée pour toutes les requêtes :
    // en créer une nouvelle par appel épuiserait les sockets disponibles
    // sous charge (limitation connue de HttpClient/IDisposable).
    // AutomaticDecompression fait ajouter par le framework un en-tête
    // Accept-Encoding cohérent avec les en-têtes "auto-générés" affichés
    // dans l'UI (Host/Content-Length le sont aussi nativement).
    private static readonly HttpClient Client =
        new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(100)
        };

    private static readonly Regex VariablePattern =
        new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public async Task<ApiResponseResult>
    SendAsync(
        string method,
        string url,
        List<HeaderItem> headers,
        RequestBody body,
        AuthConfig auth,
        Dictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        url = SubstituteVariables(url, variables);

        using var request =
            new HttpRequestMessage(
                new HttpMethod(method),
                url);

        string? explicitContentType = null;

        foreach (var header in headers)
        {
            if (!header.Enabled
                || string.IsNullOrWhiteSpace(header.Key))
            {
                continue;
            }

            var headerValue =
                SubstituteVariables(header.Value, variables)
                ?? "";

            // Content-Type doit être posé sur les headers du Content, pas
            // de la requête, sans quoi HttpClient l'ignore silencieusement.
            if (string.Equals(
                    header.Key,
                    "Content-Type",
                    StringComparison.OrdinalIgnoreCase))
            {
                explicitContentType = headerValue;
                continue;
            }

            request.Headers.TryAddWithoutValidation(
                header.Key,
                headerValue);
        }

        if (AllowsBody(method))
        {
            request.Content =
                BuildContent(body, variables, explicitContentType);
        }

        // Repli identique à ce que montre l'UI comme en-têtes
        // "auto-générés" : on ne les ajoute que si l'utilisateur ne les a
        // pas déjà définis explicitement dans sa liste de headers.
        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "KubaToolKit/1.0");
        }

        if (!request.Headers.Contains("Accept"))
        {
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "*/*");
        }

        ApplyAuth(request, auth);

        var stopwatch =
            Stopwatch.StartNew();

        using var response =
            await Client.SendAsync(
                request,
                cancellationToken);

        var responseBody =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        stopwatch.Stop();

        return new ApiResponseResult
        {
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase ?? "",
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Headers = BuildHeaderText(response),
            Body = responseBody
        };
    }

    internal static bool
    AllowsBody(
        string method) =>
        !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

    /// Construit le HttpContent selon le mode Postman sélectionné dans
    /// l'UI. urlencoded/formdata/graphql posent leur propre Content-Type
    /// (boundary multipart compris) : un Content-Type explicite de
    /// l'utilisateur n'est appliqué que pour raw/binary, où il a un sens.
    private static HttpContent?
    BuildContent(
        RequestBody body,
        Dictionary<string, string>? variables,
        string? explicitContentType)
    {
        switch (body.Mode)
        {
            case "formdata":

                var formData = new MultipartFormDataContent();

                foreach (var field in body.FormData)
                {
                    if (!field.Enabled
                        || string.IsNullOrWhiteSpace(field.Key))
                    {
                        continue;
                    }

                    formData.Add(
                        new StringContent(
                            SubstituteVariables(field.Value, variables) ?? ""),
                        field.Key);
                }

                return formData;

            case "urlencoded":

                var pairs =
                    body.UrlEncoded
                        .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key))
                        .Select(f =>
                            new KeyValuePair<string, string>(
                                f.Key,
                                SubstituteVariables(f.Value, variables) ?? ""));

                return new FormUrlEncodedContent(pairs);

            case "binary":

                if (string.IsNullOrWhiteSpace(body.BinaryFilePath)
                    || !File.Exists(body.BinaryFilePath))
                {
                    return null;
                }

                var fileContent =
                    new ByteArrayContent(File.ReadAllBytes(body.BinaryFilePath));

                fileContent.Headers.ContentType =
                    MediaTypeHeaderValue.Parse(
                        string.IsNullOrWhiteSpace(explicitContentType)
                            ? "application/octet-stream"
                            : explicitContentType);

                return fileContent;

            case "graphql":

                var query = SubstituteVariables(body.GraphQlQuery, variables) ?? "";
                var variablesText = SubstituteVariables(body.GraphQlVariables, variables);

                JsonNode? variablesNode = null;

                if (!string.IsNullOrWhiteSpace(variablesText))
                {
                    try
                    {
                        variablesNode = JsonNode.Parse(variablesText);
                    }
                    catch (JsonException)
                    {
                        variablesNode = null;
                    }
                }

                var payload =
                    new JsonObject
                    {
                        ["query"] = query,
                        ["variables"] = variablesNode ?? new JsonObject()
                    };

                return new StringContent(
                    payload.ToJsonString(),
                    Encoding.UTF8,
                    "application/json");

            case "none":

                return null;

            default: // raw

                var raw = SubstituteVariables(body.Raw, variables);

                if (string.IsNullOrEmpty(raw))
                {
                    return null;
                }

                var rawContent = new StringContent(raw, Encoding.UTF8);

                rawContent.Headers.ContentType =
                    MediaTypeHeaderValue.Parse(
                        !string.IsNullOrWhiteSpace(explicitContentType)
                            ? explicitContentType
                            : !string.IsNullOrWhiteSpace(body.RawContentType)
                                ? body.RawContentType
                                : "application/json");

                return rawContent;
        }
    }

    /// Remplace les {{clé}} (syntaxe Postman) par la valeur correspondante
    /// de l'environnement sélectionné ; laisse le texte tel quel si aucun
    /// environnement n'est actif ou si la clé est introuvable.
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    private static string?
    SubstituteVariables(
        string? text,
        Dictionary<string, string>? variables)
    {
        if (string.IsNullOrEmpty(text)
            || variables == null
            || variables.Count == 0)
        {
            return text;
        }

        return VariablePattern.Replace(
            text,
            match =>
                variables.TryGetValue(match.Groups[1].Value, out var value)
                    ? value
                    : match.Value);
    }

    private static void
    ApplyAuth(
        HttpRequestMessage request,
        AuthConfig auth)
    {
        switch (auth.Type)
        {
            case AuthType.Bearer:

                if (!string.IsNullOrWhiteSpace(auth.BearerToken))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue(
                            "Bearer",
                            auth.BearerToken);
                }

                break;

            case AuthType.Basic:

                var credentials =
                    Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(
                            $"{auth.Username}:{auth.Password}"));

                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Basic",
                        credentials);

                break;

            case AuthType.ApiKey:

                if (!string.IsNullOrWhiteSpace(auth.ApiKeyName))
                {
                    request.Headers.TryAddWithoutValidation(
                        auth.ApiKeyName,
                        auth.ApiKeyValue);
                }

                break;
        }
    }

    private static string
    BuildHeaderText(
        HttpResponseMessage response)
    {
        var lines = new List<string>();

        foreach (var header in response.Headers)
        {
            lines.Add(
                $"{header.Key}: {string.Join(", ", header.Value)}");
        }

        foreach (var header in response.Content.Headers)
        {
            lines.Add(
                $"{header.Key}: {string.Join(", ", header.Value)}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
