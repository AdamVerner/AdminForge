using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AdminForge.UI;

/// <summary>Markdown → HTML for every surface the panel renders it on.</summary>
internal static class MarkdownRenderer
{
    // Raw HTML is disabled: a handler may echo user-entered text into its result.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
            if (!IsSafe(link.Url))
                link.Url = "";
        foreach (var link in document.Descendants<AutolinkInline>())
            if (!IsSafe(link.Url))
                link.Url = "";
        return document.ToHtml(Pipeline);
    }

    // Browsers drop whitespace and control characters inside a scheme, so "java\tscript:" is still javascript.
    private static bool IsSafe(string? url)
    {
        var compact = new string((url ?? "").Where(c => c > ' ').ToArray());
        var colon = compact.IndexOf(':');
        var scheme = colon < 0 ? "" : compact[..colon];
        if (scheme.Length == 0 || scheme.IndexOfAny(['/', '?', '#']) >= 0)
            return true;
        return scheme.ToLowerInvariant() is "http" or "https" or "mailto";
    }
}
