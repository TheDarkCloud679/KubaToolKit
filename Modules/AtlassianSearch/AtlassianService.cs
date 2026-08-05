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

    // Status names/workflows are entirely custom per site (and often per
    // project), so there's no name to color mapping that would hold up --
    // but every status, however it's named, is required to belong to one
    // of Jira's three built-in categories ("new"/"indeterminate"/"done"),
    // which is what the status color coding keys off instead.
    public async Task<Dictionary<string, string>>
    GetJiraStatusCategories(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        try
        {
            var items = await GetJsonArray(settings, $"{baseUrl}/rest/api/3/status", cancellationToken);

            var categoriesByStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var name = TryGetString(item, "name");
                var categoryKey = TryGetString(item, "statusCategory", "key");

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(categoryKey))
                {
                    categoriesByStatus[name] = categoryKey;
                }
            }

            return categoriesByStatus;
        }
        catch (Exception ex)
        {
            Logger.Error($"AtlassianService: failed to load status categories from {baseUrl}.", ex);

            return new Dictionary<string, string>();
        }
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
        var hasQuery = !string.IsNullOrWhiteSpace(query);

        // "text" already covers the title (per Atlassian's own CQL
        // reference), and OR'ing in a separate "title ~" clause turned out
        // to make pages vanish entirely once an "ancestor" filter was also
        // in play -- some parenthesized-OR-plus-ancestor combination the
        // query planner doesn't handle the way a flat AND chain does. Kept
        // simple; the client-side sort below still surfaces title matches
        // first among whatever comes back.
        //
        // The trailing "*" turns this into a prefix/wildcard match instead
        // of CQL's default fuzzy (edit-distance-limited) match -- without
        // it, a short fragment like "err" simply doesn't match a longer
        // word like "erreur" (that's an edit distance of 3, past Lucene's
        // usual fuzzy cutoff of 2), even though it obviously should as a
        // prefix.
        var cql =
            hasQuery
                ? $"text ~ \"{EscapeForQuery(query)}*\" and type in (page, blogpost)"
                : "type in (page, blogpost)";

        if (spaceKeys.Count == 1)
        {
            cql += $" and space = \"{EscapeForQuery(spaceKeys[0])}\"";
        }
        else if (spaceKeys.Count > 1)
        {
            var quoted = string.Join(", ", spaceKeys.Select(k => $"\"{EscapeForQuery(k)}\""));

            cql += $" and space in ({quoted})";
        }

        // Restricts to a group/page/article and its descendants. The
        // selected one might itself be a leaf -- an "article" with no
        // children of its own rather than an actual category -- and
        // "ancestor" alone only ever matches descendants, never the node
        // itself, which silently returned zero results for that case.
        // "id =" covers picking the leaf directly; "ancestor =" still
        // covers it turning out to have children after all.
        if (!string.IsNullOrWhiteSpace(ancestorId))
        {
            cql += $" and (id = {EscapeForQuery(ancestorId)} or ancestor = {EscapeForQuery(ancestorId)})";
        }

        // With no search text, there's nothing to rank by relevance --
        // fall back to the most recently added pages instead of an
        // unordered dump of the whole space.
        if (!hasQuery)
        {
            cql += " order by created desc";
        }

        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var url =
            $"{baseUrl}/wiki/rest/api/search"
            + $"?cql={Uri.EscapeDataString(cql)}"
            + $"&limit={(hasQuery ? 50 : 10)}"
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
            var id = TryGetString(item, "content", "id") ?? "";

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
                    Id = id,
                    // Confluence's search API returns title/excerpt as
                    // HTML-entity-escaped text (e.g. "d&#39;obtenir").
                    Title = System.Net.WebUtility.HtmlDecode(title),
                    Space = space,
                    Excerpt = System.Net.WebUtility.HtmlDecode(StripMarkup(excerpt)),
                    Url = string.IsNullOrWhiteSpace(relativeUrl) ? "" : $"{baseUrl}/wiki{relativeUrl}",
                    LastModifiedDisplay = lastModified
                });
        }

        // Stable sort (ties keep Confluence's own relevance order) so an
        // obvious title match always leads, instead of wherever the body-
        // relevance score happened to place it.
        return
            hasQuery
                ? results.OrderByDescending(r => r.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList()
                : results;
    }

    public async Task<ConfluencePageContent>
    GetConfluencePageContent(
        AtlassianSettings settings,
        string pageId,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var url = $"{baseUrl}/wiki/rest/api/content/{Uri.EscapeDataString(pageId)}?expand=body.view";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Authorization = BuildAuthHeader(settings);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Client.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to load Confluence page: HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        using var doc = JsonDocument.Parse(body);

        return new ConfluencePageContent
        {
            Title = TryGetString(doc.RootElement, "title") ?? "",
            Html = TryGetString(doc.RootElement, "body", "view", "value") ?? ""
        };
    }

    public async Task<List<JiraSearchResult>>
    SearchJira(
        AtlassianSettings settings,
        string query,
        string? project,
        string? reporter,
        string? assignee,
        bool assigneeIsUnassigned,
        string? priority,
        string priorityOperator,
        string? status,
        CancellationToken cancellationToken = default)
    {
        // A saved filter (e.g. "Unassigned > P2") may carry no search text
        // at all -- built as a list of conditions instead of an always-
        // present "text ~" prefix, so a text-less filter still produces
        // valid JQL instead of an empty/invalid "text ~ *" clause. The
        // caller only invokes this once at least one of query/project/
        // reporter/assignee/priority/status is actually set.
        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            // Without the trailing "*", JQL's fuzzy "~" match won't catch
            // a short fragment against a longer word once they're more
            // than ~2 edits apart.
            conditions.Add($"text ~ \"{EscapeForQuery(query)}*\"");
        }

        if (!string.IsNullOrWhiteSpace(project))
        {
            conditions.Add($"project = \"{EscapeForQuery(project)}\"");
        }

        if (!string.IsNullOrWhiteSpace(reporter))
        {
            conditions.Add($"reporter = \"{EscapeForQuery(reporter)}\"");
        }

        if (assigneeIsUnassigned)
        {
            conditions.Add("assignee is EMPTY");
        }
        else if (!string.IsNullOrWhiteSpace(assignee))
        {
            conditions.Add($"assignee = \"{EscapeForQuery(assignee)}\"");
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            // Restricted to a known-safe set rather than trusted as-is --
            // this ends up directly in the JQL string, unquoted.
            var op = priorityOperator switch
            {
                ">" or ">=" or "<" or "<=" => priorityOperator,
                _ => "="
            };

            conditions.Add($"priority {op} \"{EscapeForQuery(priority)}\"");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            conditions.Add($"status = \"{EscapeForQuery(status)}\"");
        }

        var jql = string.Join(" and ", conditions) + " order by updated desc";

        var baseUrl = settings.BaseUrl.TrimEnd('/');

        // /rest/api/3/search was removed by Atlassian (HTTP 410) in favor of
        // this endpoint -- same query params, just a different path.
        var url =
            $"{baseUrl}/rest/api/3/search/jql"
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

        if (!doc.RootElement.TryGetProperty("issues", out var issuesEl)
            || issuesEl.ValueKind != JsonValueKind.Array)
        {
            return new List<JiraSearchResult>();
        }

        return ParseJiraIssues(issuesEl, baseUrl);
    }

    // Shared between plain JQL search and queue browsing below -- both
    // return the same "issues"-shaped array (just under different
    // property names at the top level), each item as {key, fields:{...}}.
    private static List<JiraSearchResult>
    ParseJiraIssues(
        JsonElement issuesEl,
        string baseUrl)
    {
        var results = new List<JiraSearchResult>();

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
                    // Not every issue/request type carries a priority --
                    // defaulted the same way Assignee is, rather than left
                    // blank, so the badge always renders something.
                    Priority = TryGetString(issue, "fields", "priority", "name") ?? "No priority",
                    Status = TryGetString(issue, "fields", "status", "name") ?? "",
                    UpdatedDisplay = TryGetString(issue, "fields", "updated") ?? "",
                    Url = string.IsNullOrWhiteSpace(key) ? "" : $"{baseUrl}/browse/{key}"
                });
        }

        return results;
    }

    // Jira Service Management-specific: a "queue" is a saved, admin-defined
    // view scoped to one service desk project (e.g. "L1 Incidents") --
    // separate API surface (/rest/servicedeskapi) from plain Jira search,
    // since queues aren't expressed as JQL the REST API exposes.
    public async Task<Dictionary<string, string>>
    GetJiraServiceDesksByProjectKey(
        AtlassianSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var byProjectKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var start = 0;

            while (true)
            {
                var url = $"{baseUrl}/rest/servicedeskapi/servicedesk?start={start}&limit=50";

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

                if (!doc.RootElement.TryGetProperty("values", out var valuesEl)
                    || valuesEl.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var count = 0;

                foreach (var el in valuesEl.EnumerateArray())
                {
                    count++;

                    var id = TryGetString(el, "id");
                    var projectKey = TryGetString(el, "projectKey");

                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(projectKey))
                    {
                        byProjectKey[projectKey!] = id!;
                    }
                }

                var isLast =
                    doc.RootElement.TryGetProperty("isLastPage", out var lastEl)
                    && lastEl.ValueKind == JsonValueKind.True;

                if (isLast || count == 0)
                {
                    break;
                }

                start += 50;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AtlassianService: failed to load service desks.", ex);
        }

        return byProjectKey;
    }

    public async Task<List<NameValue>>
    GetJiraQueues(
        AtlassianSettings settings,
        string serviceDeskId,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var results = new List<NameValue>();

        try
        {
            var start = 0;

            while (true)
            {
                var url =
                    $"{baseUrl}/rest/servicedeskapi/servicedesk/{Uri.EscapeDataString(serviceDeskId)}/queue"
                    + $"?start={start}&limit=50";

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

                if (!doc.RootElement.TryGetProperty("values", out var valuesEl)
                    || valuesEl.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var count = 0;

                foreach (var el in valuesEl.EnumerateArray())
                {
                    count++;

                    var id = TryGetString(el, "id");
                    var name = TryGetString(el, "name");

                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    {
                        results.Add(new NameValue(id!, name!));
                    }
                }

                var isLast =
                    doc.RootElement.TryGetProperty("isLastPage", out var lastEl)
                    && lastEl.ValueKind == JsonValueKind.True;

                if (isLast || count == 0)
                {
                    break;
                }

                start += 50;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AtlassianService: failed to load queues for service desk {serviceDeskId}.", ex);
        }

        return results
            .OrderBy(q => q.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<JiraSearchResult>>
    GetQueueIssues(
        AtlassianSettings settings,
        string serviceDeskId,
        string queueId,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        var url =
            $"{baseUrl}/rest/servicedeskapi/servicedesk/{Uri.EscapeDataString(serviceDeskId)}"
            + $"/queue/{Uri.EscapeDataString(queueId)}/issue"
            + "?limit=50&fields=summary,reporter,assignee,priority,status,project,updated";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.Authorization = BuildAuthHeader(settings);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Client.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to load queue: HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("values", out var valuesEl)
            || valuesEl.ValueKind != JsonValueKind.Array)
        {
            return new List<JiraSearchResult>();
        }

        return ParseJiraIssues(valuesEl, baseUrl);
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
