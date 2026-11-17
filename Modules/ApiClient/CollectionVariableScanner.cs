using KubaToolKit.Modules.ApiClient.Models;
using System.Text.RegularExpressions;

namespace KubaToolKit.Modules.ApiClient;

// Finds every {{variableName}} referenced anywhere across a set of loaded
// collections -- used by "Sync from AWS" to tell which environment
// variables the collections actually need, so it can filter down to
// what's missing instead of showing every API Gateway endpoint found.
public static class CollectionVariableScanner
{
    private static readonly Regex VariablePattern =
        new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public static HashSet<string>
    FindReferencedVariables(
        IEnumerable<CollectionNode> roots)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in roots)
        {
            Scan(root, names);
        }

        return names;
    }

    private static void
    Scan(
        CollectionNode node,
        HashSet<string> names)
    {
        Collect(node.Url, names);

        foreach (var header in node.Headers)
        {
            Collect(header.Value, names);
        }

        Collect(node.Body, names);

        foreach (var field in node.BodyFormData)
        {
            Collect(field.Value, names);
        }

        Collect(node.Auth.BearerToken, names);
        Collect(node.Auth.Username, names);
        Collect(node.Auth.Password, names);
        Collect(node.Auth.ApiKeyValue, names);

        foreach (var child in node.Children)
        {
            Scan(child, names);
        }
    }

    private static void
    Collect(
        string? text,
        HashSet<string> names)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Match match in VariablePattern.Matches(text))
        {
            names.Add(match.Groups[1].Value);
        }
    }
}
