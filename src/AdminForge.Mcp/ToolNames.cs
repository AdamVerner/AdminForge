using System.Text;
using System.Text.RegularExpressions;

namespace AdminForge.Mcp;

internal static partial class ToolNames
{
    /// <summary>"SiteSettings" → "site_settings", "Enable experimental API (1 h)" → "enable_experimental_api_1_h".</summary>
    public static string Slug(string label)
    {
        var sb = new StringBuilder(label.Length + 8);
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (char.IsUpper(c) && i > 0 && char.IsLower(label[i - 1]))
                sb.Append('_');
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }
        return Underscores().Replace(sb.ToString(), "_").Trim('_');
    }

    [GeneratedRegex("_+")]
    private static partial Regex Underscores();
}
