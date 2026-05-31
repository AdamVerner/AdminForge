using Microsoft.AspNetCore.WebUtilities;

namespace AdminForge.UI.Blazor;

/// <summary>
/// Pulls the <c>filter:{column}={value}</c> entries off a URL into a dictionary
/// suitable for seeding <see cref="Core.Contracts.ListQuery.Filters"/>. Living
/// outside the Razor page so it's straightforward to unit-test.
/// </summary>
public static class FilterUrlParser
{
    /// <summary>
    /// Parse <paramref name="uri"/>'s query string and return only the <c>filter:</c>-prefixed
    /// entries (the prefix is stripped from the keys). Other query parameters are ignored;
    /// values are kept as strings (the data provider coerces them to property types).
    /// </summary>
    public static Dictionary<string, object?> Parse(string uri)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(uri))
            return result;
        var queryIdx = uri.IndexOf('?');
        if (queryIdx < 0)
            return result;
        var query = QueryHelpers.ParseQuery(uri[queryIdx..]);
        foreach (var kvp in query)
        {
            if (!kvp.Key.StartsWith("filter:", StringComparison.Ordinal))
                continue;
            var col = kvp.Key["filter:".Length..];
            if (string.IsNullOrEmpty(col))
                continue;
            result[col] = kvp.Value.ToString();
        }
        return result;
    }
}
