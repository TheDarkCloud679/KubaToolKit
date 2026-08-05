using KubaToolKit.Modules.AtlassianSearch.Models;
using KubaToolKit.Shared.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KubaToolKit.Modules.AtlassianSearch;

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

    // Filter dropdown options. Each is best-effort: if a call fails (a
    // permission restriction, an endpoint not available on some site
    // configuration...) it comes back empty rather than throwing, so a
    // dropdown just falls back to "type your own" instead of blocking
    // search entirely.
    public async Task<List<NameValue>>
    GetConfluenceSpaces(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');
        var results = new List<NameValue>();

        try
        {
            const int PageSize = 100;
            var start = 0;

            // A site can easily have more spaces than fit on one page --
            // without paging through all of them, a space further down the
            // (unspecified) ordering would never show up in the dropdown
            // at all, default or not.
            while (true)
            {
                var items =
                    await GetJsonArray(
                        settings,
                        $"{baseUrl}/wiki/rest/api/space?start={start}&limit={PageSize}&type=global",
                        cancellationToken);

                foreach (var item in items)
                {
                    var key = TryGetString(item, "key");

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    results.Add(new NameValue(key, TryGetString(item, "name") ?? key));
                }

                if (items.Count < PageSize)
                {
                    break;
                }

                start += PageSize;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianService: failed to load Confluence spaces.", ex);
        }

        return results
            .OrderBy(r => r.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // "Groups" = the space's top-level pages (what shows as the root
    // entries of the page tree in Confluence's own sidebar): the direct
    // children of the space's homepage. Falls back to pages with no
    // parent at all if the space has no homepage (or the call fails),
    // since that's the closest equivalent.
    public async Task<List<NameValue>>
    GetConfluenceSpaceGroups(
        AtlassianSettings settings,
        string spaceKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spaceKey))
        {
            return new List<NameValue>();
        }

        var baseUrl = settings.BaseUrl.TrimEnd('/');

        try
        {
            var spaceElements =
                await GetJsonArray(
                    settings,
                    $"{baseUrl}/wiki/rest/api/space?spaceKey={Uri.EscapeDataString(spaceKey)}&expand=homepage",
                    cancellationToken);

            var homepageId =
                spaceElements.Count > 0
                    ? TryGetString(spaceElements[0], "homepage", "id")
                    : null;

            if (!string.IsNullOrWhiteSpace(homepageId))
            {
                var children = await GetConfluenceChildPages(settings, homepageId, cancellationToken);

                if (children.Count > 0)
                {
                    return children;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AtlassianService: failed to load the homepage for space '{spaceKey}'.", ex);
        }

        // No homepage, or it has no children: fall back to pages that have
        // no parent page of their own anywhere in the space.
        return await GetNameValueList(
            settings,
            $"{baseUrl}/wiki/rest/api/content?spaceKey={Uri.EscapeDataString(spaceKey)}&type=page&expand=ancestors&limit=250",
            "results",
            el => TryGetString(el, "id"),
            el => TryGetString(el, "title"),
            cancellationToken,
            swallowErrors: true,
            include: el =>
                el.TryGetProperty("ancestors", out var ancestorsEl)
                && ancestorsEl.ValueKind == JsonValueKind.Array
                && ancestorsEl.GetArrayLength() == 0);
    }

    public async Task<List<NameValue>>
    GetConfluenceChildPages(
        AtlassianSettings settings,
        string parentPageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentPageId))
        {
            return new List<NameValue>();
        }

        var baseUrl = settings.BaseUrl.TrimEnd('/');

        return await GetNameValueList(
            settings,
            $"{baseUrl}/wiki/rest/api/content/{parentPageId}/child/page?limit=250",
            "results",
            el => TryGetString(el, "id"),
            el => TryGetString(el, "title"),
            cancellationToken,
            swallowErrors: true);
    }

    public async Task<List<NameValue>>
    GetJiraProjects(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');
        var results = new List<NameValue>();

        try
        {
            const int PageSize = 100;
            var startAt = 0;

            // Same reasoning as Confluence spaces: an org can have more
            // projects than fit on one page, so this pages through all of
            // them instead of silently truncating.
            while (true)
            {
                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        $"{baseUrl}/rest/api/3/project/search?startAt={startAt}&maxResults={PageSize}");

                request.Headers.Authorization = BuildAuthHeader(settings);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await Client.SendAsync(request, cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
                }

                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("values", out var valuesEl)
                    || valuesEl.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var pageCount = 0;

                foreach (var item in valuesEl.EnumerateArray())
                {
                    pageCount++;

                    var key = TryGetString(item, "key");

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    results.Add(new NameValue(key, TryGetString(item, "name") ?? key));
                }

                // Default to stopping unless the response explicitly says
                // there's more -- an unrecognized/missing field is safer
                // treated as "last page" than looped on indefinitely.
                var isLast = true;

                if (doc.RootElement.TryGetProperty("isLast", out var isLastEl)
                    && isLastEl.ValueKind == JsonValueKind.False)
                {
                    isLast = false;
                }

                if (isLast || pageCount < PageSize)
                {
                    break;
                }

                startAt += PageSize;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianService: failed to load Jira projects.", ex);
        }

        return results
            .OrderBy(r => r.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<NameValue>>
    GetJiraPriorities(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        return await GetNameValueListFromArray(
            settings,
            $"{baseUrl}/rest/api/3/priority",
            el => TryGetString(el, "name"),
            cancellationToken);
    }

    public async Task<List<NameValue>>
    GetJiraStatuses(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        return await GetNameValueListFromArray(
            settings,
            $"{baseUrl}/rest/api/3/status",
            el => TryGetString(el, "name"),
            cancellationToken);
    }

    // Jira Cloud has no "list everyone" dropdown source that's both
    // complete and privacy-compliant -- this seeds the list with the first
    // page of the org's users, which covers most teams; anyone not in that
    // page can still be typed directly (the field stays editable).
    public async Task<List<NameValue>>
    GetJiraUsers(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var users =
            await GetJsonArray(
                settings,
                $"{baseUrl}/rest/api/3/users/search?maxResults=200",
                cancellationToken);

        return users
            .Where(el =>
                (TryGetString(el, "accountType") ?? "atlassian") == "atlassian"
                && !string.IsNullOrWhiteSpace(TryGetString(el, "displayName")))
            .Select(el => TryGetString(el, "displayName")!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new NameValue(name, name))
            .ToList();
    }

    private async Task<List<JsonElement>>
    GetJsonArray(
        AtlassianSettings settings,
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Authorization = BuildAuthHeader(settings);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Client.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        using var doc = JsonDocument.Parse(body);

        var root = doc.RootElement;

        var arrayElement =
            root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("values", out var valuesEl) ? valuesEl
                : root.TryGetProperty("results", out var resultsEl) ? resultsEl
                : default;

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            return new List<JsonElement>();
        }

        // Cloned: the JsonDocument (and the elements it owns) is disposed
        // when this method returns, so callers need their own copies.
        return arrayElement.EnumerateArray().Select(el => el.Clone()).ToList();
    }

    private async Task<List<NameValue>>
    GetNameValueListFromArray(
        AtlassianSettings settings,
        string url,
        Func<JsonElement, string?> getName,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await GetJsonArray(settings, url, cancellationToken);

            return items
                .Select(getName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new NameValue(name!, name!))
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error($"AtlassianService: failed to load options from {url}.", ex);

            return new List<NameValue>();
        }
    }

    private async Task<List<NameValue>>
    GetNameValueList(
        AtlassianSettings settings,
        string url,
        string arrayProperty,
        Func<JsonElement, string?> getValue,
        Func<JsonElement, string?> getDisplay,
        CancellationToken cancellationToken,
        bool swallowErrors = false,
        Func<JsonElement, bool>? include = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Authorization = BuildAuthHeader(settings);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await Client.SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
            }

            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty(arrayProperty, out var arrayEl)
                || arrayEl.ValueKind != JsonValueKind.Array)
            {
                return new List<NameValue>();
            }

            var results = new List<NameValue>();

            foreach (var item in arrayEl.EnumerateArray())
            {
                if (include != null && !include(item))
                {
                    continue;
                }

                var value = getValue(item);
                var display = getDisplay(item);

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                results.Add(new NameValue(value, string.IsNullOrWhiteSpace(display) ? value : display));
            }

            return results
                .OrderBy(r => r.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            if (!swallowErrors)
            {
                Logger.Error($"AtlassianService: failed to load options from {url}.", ex);
            }

            return new List<NameValue>();
        }
    }

    public async Task<List<ConfluenceSearchResult>>
    SearchConfluence(
        AtlassianSettings settings,
        string query,
        IReadOnlyList<string> spaceKeys,
        string? ancestorId,
        CancellationToken cancellationToken = default)
    {
        var cql = $"text ~ \"{EscapeForQuery(query)}\" and type in (page, blogpost)";

        if (spaceKeys.Count == 1)
        {
            cql += $" and space = \"{EscapeForQuery(spaceKeys[0])}\"";
        }
        else if (spaceKeys.Count > 1)
        {
            var quoted = string.Join(", ", spaceKeys.Select(k => $"\"{EscapeForQuery(k)}\""));

            cql += $" and space in ({quoted})";
        }

        // Restricts to a group (a top-level page) or, more specifically,
        // one of its pages -- "ancestor" matches the whole subtree, not
        // just direct children, so either works with the same clause.
        if (!string.IsNullOrWhiteSpace(ancestorId))
        {
            cql += $" and ancestor = {EscapeForQuery(ancestorId)}";
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
