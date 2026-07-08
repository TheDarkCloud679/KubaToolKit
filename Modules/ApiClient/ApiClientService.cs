using KubaToolKit.Modules.ApiClient.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace KubaToolKit.Modules.ApiClient;

public class ApiClientService
{
    // Instance HttpClient partagée et réutilisée pour toutes les requêtes :
    // en créer une nouvelle par appel épuiserait les sockets disponibles
    // sous charge (limitation connue de HttpClient/IDisposable).
    private static readonly HttpClient Client = new()
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
        string? body,
        AuthConfig auth,
        Dictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        url = SubstituteVariables(url, variables);
        body = SubstituteVariables(body, variables);

        using var request =
            new HttpRequestMessage(
                new HttpMethod(method),
                url);

        if (!string.IsNullOrEmpty(body)
            && AllowsBody(method))
        {
            request.Content =
                new StringContent(body, Encoding.UTF8);
        }

        string? contentType = null;

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
                contentType = headerValue;
                continue;
            }

            request.Headers.TryAddWithoutValidation(
                header.Key,
                headerValue);
        }

        if (request.Content != null)
        {
            request.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse(
                    string.IsNullOrWhiteSpace(contentType)
                        ? "application/json"
                        : contentType);
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

    private static bool
    AllowsBody(
        string method) =>
        !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

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
