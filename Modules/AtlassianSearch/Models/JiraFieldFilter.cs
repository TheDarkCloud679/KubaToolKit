namespace KubaToolKit.Modules.AtlassianSearch.Models;

// A filter value paired with the JQL operator to apply it with -- "=" for
// a straight match, "!=" to exclude it, "in"/"not in" for a comma-
// separated list of values, and (Priority only) ">"/">="/"<"/"<=" since
// priority is one of the few JQL fields that supports relative comparison.
public readonly record struct JiraFieldFilter(string Value, string Operator)
{
    public static readonly JiraFieldFilter Empty = new("", "=");
}
