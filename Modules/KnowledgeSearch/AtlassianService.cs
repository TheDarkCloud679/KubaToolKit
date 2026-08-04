using KubaToolKit.Modules.KnowledgeSearch.Models;
using KubaToolKit.Shared.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KubaToolKit.Modules.KnowledgeSearch;

/// Read-only search against Atlassian Cloud (Jira + Confluence share the
/// same site/credentials, per how this instance is set up). Built against
/// the documented Cloud REST API, but -- like the FileZilla export -- it
/// couldn't be exercised against a real tenant from this environment, so
/// field paths are read defensively (falling back to blank rather than
/// throwing) in case a real response shape differs in some corner.
public class AtlassianService
{
    private static readonly HttpClient Client = new();

    private static AuthenticationHeaderValue
    BuildAuthHeader(
        AtlassianSettings settings)
    {
        var raw = $"{settings.Email}:{settings.ApiToken}";

        return new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
    }

    private static string
    EscapeForQuery(
        string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public async Task<(bool Success, string Message)>
    TestConnection(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var baseUrl = settings.BaseUrl.TrimEnd('/');

            using var request =
                new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/rest/api/3/myself");

            request.Headers.Authorization = BuildAuthHeader(settings);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await Client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(body);

            var displayName =
                doc.RootElement.TryGetProperty("displayName", out var nameEl)
                    ? nameEl.GetString()
                    : null;

            return (true, string.IsNullOrWhiteSpace(displayName) ? "Connected." : $"Connected as {displayName}.");
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianService: connection test failed.", ex);

            return (false, ex.Message);
        }
    }

    public async Task<List<ConfluenceSearchResult>>
    SearchConfluence(
        AtlassianSettings settings,
        string query,
        string? spaceFilter,
        string? labelFilter,
        CancellationToken cancellationToken = default)
    {
        var cql = $"text ~ \"{EscapeForQuery(query)}\" and type in (page, blogpost)";

        if (!string.IsNullOrWhiteSpace(spaceFilter))
        {
            cql += $" and space = \"{EscapeForQuery(spaceFilter)}\"";
        }

        if (!string.IsNullOrWhiteSpace(labelFilter))
        {
            cql += $" and label = \"{EscapeForQuery(labelFilter)}\"";
        }

        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var url =
            $"{baseUrl}/wiki/rest/api/search"
            + $"?cql={Uri.EscapeDataString(cql)}"
            + "&limit=25"
            + "&excerpt=highlight";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Authorization = BuildAuthHeader(settings);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Client.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Confluence search failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        using var doc = JsonDocument.Parse(body);

        var results = new List<ConfluenceSearchResult>();

        if (!doc.RootElement.TryGetProperty("results", out var resultsEl)
            || resultsEl.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in resultsEl.EnumerateArray())
        {
            var title =
                TryGetString(item, "title")
                ?? TryGetString(item, "content", "title")
                ?? "(untitled)";

            var space =
                TryGetString(item, "resultGlobalContainer", "title")
                ?? TryGetString(item, "content", "space", "key")
                ?? "";

            var excerpt =
                TryGetString(item, "excerpt")
                ?? "";

            var relativeUrl =
                TryGetString(item, "url")
                ?? TryGetString(item, "content", "_links", "webui")
                ?? "";

            var lastModified =
                TryGetString(item, "lastModified")
                ?? TryGetString(item, "friendlyLastModified")
                ?? "";

            results.Add(
                new ConfluenceSearchResult
                {
                    Title = title,
                    Space = space,
                    Excerpt = StripMarkup(excerpt),
                    Url = string.IsNullOrWhiteSpace(relativeUrl) ? "" : $"{baseUrl}/wiki{relativeUrl}",
                    LastModifiedDisplay = lastModified
                });
        }

        return results;
    }

    public async Task<List<JiraSearchResult>>
    SearchJira(
        AtlassianSettings settings,
        string query,
        string? project,
        string? reporter,
        string? assignee,
        string? priority,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var jql = $"text ~ \"{EscapeForQuery(query)}\"";

        if (!string.IsNullOrWhiteSpace(project))
        {
            jql += $" and project = \"{EscapeForQuery(project)}\"";
        }

        if (!string.IsNullOrWhiteSpace(reporter))
        {
            jql += $" and reporter = \"{EscapeForQuery(reporter)}\"";
        }

        if (!string.IsNullOrWhiteSpace(assignee))
        {
            jql += $" and assignee = \"{EscapeForQuery(assignee)}\"";
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            jql += $" and priority = \"{EscapeForQuery(priority)}\"";
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            jql += $" and status = \"{EscapeForQuery(status)}\"";
        }

        jql += " order by updated desc";

        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var url =
            $"{baseUrl}/rest/api/3/search"
            + $"?jql={Uri.EscapeDataString(jql)}"
            + "&maxResults=25"
            + "&fields=summary,reporter,assignee,priority,status,project,updated";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Authorization = BuildAuthHeader(settings);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Client.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Jira search failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        using var doc = JsonDocument.Parse(body);

        var results = new List<JiraSearchResult>();

        if (!doc.RootElement.TryGetProperty("issues", out var issuesEl)
            || issuesEl.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var issue in issuesEl.EnumerateArray())
        {
            var key = TryGetString(issue, "key") ?? "";

            results.Add(
                new JiraSearchResult
                {
                    Key = key,
                    Summary = TryGetString(issue, "fields", "summary") ?? "",
                    Project = TryGetString(issue, "fields", "project", "key") ?? "",
                    Reporter = TryGetString(issue, "fields", "reporter", "displayName") ?? "",
                    Assignee = TryGetString(issue, "fields", "assignee", "displayName") ?? "Unassigned",
                    Priority = TryGetString(issue, "fields", "priority", "name") ?? "",
                    Status = TryGetString(issue, "fields", "status", "name") ?? "",
                    UpdatedDisplay = TryGetString(issue, "fields", "updated") ?? "",
                    Url = string.IsNullOrWhiteSpace(key) ? "" : $"{baseUrl}/browse/{key}"
                });
        }

        return results;
    }

    private static string?
    TryGetString(
        JsonElement element,
        params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : null;
    }

    // Confluence excerpts come back with @@@hl@@@/@@@endhl@@@ highlight
    // markers (and occasionally raw HTML) around matched terms -- neither
    // renders as plain text, so both are stripped for a clean snippet.
    private static string
    StripMarkup(
        string excerpt) =>
        System.Text.RegularExpressions.Regex
            .Replace(excerpt, "@@@(end)?hl@@@|<[^>]+>", "")
            .Trim();
}
