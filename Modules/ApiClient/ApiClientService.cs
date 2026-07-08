using KubaToolKit.Modules.ApiClient.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

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

    public async Task<ApiResponseResult>
    SendAsync(
        string method,
        string url,
        List<HeaderItem> headers,
        string? body,
        AuthConfig auth,
        CancellationToken cancellationToken = default)
    {
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

            // Content-Type doit être posé sur les headers du Content, pas
            // de la requête, sans quoi HttpClient l'ignore silencieusement.
            if (string.Equals(
                    header.Key,
                    "Content-Type",
                    StringComparison.OrdinalIgnoreCase))
            {
                contentType = header.Value;
                continue;
            }

            request.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value);
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
