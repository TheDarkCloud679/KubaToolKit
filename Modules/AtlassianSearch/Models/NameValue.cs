namespace KubaToolKit.Modules.AtlassianSearch.Models;

// A dropdown entry: Value is what goes into the CQL/JQL filter, Display is
// what's shown (sometimes the same, e.g. a Jira status name; sometimes not,
// e.g. a Confluence space's key vs. its friendly name).
public readonly record struct NameValue(string Value, string Display);
