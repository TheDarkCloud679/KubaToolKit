using KubaToolKit.Modules.Pandora.Models;
using System.Net.Http;
using System.Text.Json;

namespace KubaToolKit.Modules.Pandora;

/// Client pour l'API legacy de Pandora FMS (include/api.php). Le format de
/// réponse exact (tableau JSON brut vs {"data": [...]}, lignes en tableau
/// positionnel vs objet à clés) n'a pas pu être vérifié contre un serveur
/// réel depuis cet environnement -- ReadRows/GetField lisent donc les deux
/// formes possibles plutôt que de supposer une forme unique.
public class PandoraService
{
    private static readonly HttpClient Http = new();

    public async Task<List<PandoraGroupNode>>
    GetTreeAsync(
        PandoraProfile profile,
        CancellationToken cancellationToken = default)
    {
        var groups = await GetGroupsAsync(profile, cancellationToken);
        var tree = new List<PandoraGroupNode>();

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var agents =
                await GetAgentsForGroupAsync(profile, group.Id, cancellationToken);

            // Un groupe sans agent n'apporte rien à l'arbre, comme les
            // catégories vides côté CloudWatch.
            if (agents.Count == 0)
            {
                continue;
            }

            tree.Add(
                new PandoraGroupNode
                {
                    Name = group.Name,
                    Agents = new(agents)
                });
        }

        return tree.OrderBy(g => g.Name).ToList();
    }

    private async Task<List<(string Id, string Name)>>
    GetGroupsAsync(
        PandoraProfile profile,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(profile, "get", "groups");
        var json = await FetchAsync(url, cancellationToken);
        var rows = ReadRows(json);

        var result = new List<(string, string)>();

        foreach (var row in rows)
        {
            var id = GetField(row, 0, "id");
            var name = GetField(row, 1, "group");

            if (!string.IsNullOrWhiteSpace(id))
            {
                result.Add((id, string.IsNullOrWhiteSpace(name) ? id : name));
            }
        }

        return result;
    }

    private async Task<List<PandoraAgent>>
    GetAgentsForGroupAsync(
        PandoraProfile profile,
        string groupId,
        CancellationToken cancellationToken)
    {
        // other[] attendu par api_get_all_agents : filter_so, filter_group,
        // filter_modules_states, filter_name, filter_policy, csv_separator,
        // recursion -- seul filter_group (index 1) est renseigné ici.
        var other = $"|{groupId}|||||0";

        var url =
            BuildUrl(profile, "get", "all_agents")
            + $"&other={Uri.EscapeDataString(other)}"
            + $"&other_mode={Uri.EscapeDataString("url_encode_separator_|")}";

        var json = await FetchAsync(url, cancellationToken);
        var rows = ReadRows(json);

        var result = new List<PandoraAgent>();

        foreach (var row in rows)
        {
            var alias = GetField(row, 1, "alias");

            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            result.Add(
                new PandoraAgent
                {
                    Id = GetField(row, 0, "id_agente"),
                    Alias = alias,
                    Address = GetField(row, 2, "direccion"),
                    Comments = GetField(row, 3, "comentarios"),
                    OsName = GetField(row, 4, "name"),
                    Status = ParseStatus(GetField(row, 7, "status"))
                });
        }

        return result.OrderBy(a => a.Alias).ToList();
    }

    private int
    ParseStatus(string raw) =>
        int.TryParse(raw, out var value) ? value : 3;

    private string
    BuildUrl(
        PandoraProfile profile,
        string op,
        string op2)
    {
        var baseUrl = profile.Url.TrimEnd('/');

        var url =
            $"{baseUrl}/include/api.php"
            + $"?op={op}&op2={op2}"
            + $"&user={Uri.EscapeDataString(profile.User)}"
            + $"&pass={Uri.EscapeDataString(profile.Pass)}"
            + "&return_type=json";

        if (!string.IsNullOrWhiteSpace(profile.ApiPassword))
        {
            url += $"&apipass={Uri.EscapeDataString(profile.ApiPassword)}";
        }

        return url;
    }

    private async Task<string>
    FetchAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        response.EnsureSuccessStatusCode();

        return body;
    }

    private List<JsonElement>
    ReadRows(
        string json)
    {
        JsonDocument doc;

        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Pandora renvoie parfois un message d'erreur texte brut (auth
            // invalide, apipass manquant...) même avec return_type=json.
            throw new Exception($"Réponse Pandora inattendue : {json.Trim()}");
        }

        var root = doc.RootElement;

        var data =
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var inner)
                ? inner
                : root;

        return data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().ToList()
            : new List<JsonElement>();
    }

    private string
    GetField(
        JsonElement row,
        int arrayIndex,
        string objectKey)
    {
        if (row.ValueKind == JsonValueKind.Array)
        {
            return arrayIndex < row.GetArrayLength()
                ? row[arrayIndex].ToString()
                : "";
        }

        if (row.ValueKind == JsonValueKind.Object
            && row.TryGetProperty(objectKey, out var value))
        {
            return value.ToString();
        }

        return "";
    }
}
