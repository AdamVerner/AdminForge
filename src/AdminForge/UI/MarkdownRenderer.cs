using Markdig;
using Markdig.Extensions.GenericAttributes;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MudBlazor;

namespace AdminForge.UI;

/// <summary>Markdown → HTML for every surface the panel renders it on.</summary>
internal static class MarkdownRenderer
{
    // Raw HTML is disabled: a handler may echo user-entered text into its result. Generic
    // attributes go too: {onerror=...} would put any attribute, a handler included, on an element.
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml();
        builder.Extensions.RemoveAll(e => e is GenericAttributesExtension);
        return builder.Build();
    }

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafe(link.Url))
                link.Url = "";
            // An image from elsewhere would have the admin's browser call that host on sight; it becomes a link.
            if (link.IsImage && !IsLocal(link.Url))
                link.IsImage = false;
        }
        foreach (var link in document.Descendants<AutolinkInline>())
            if (!IsSafe(link.Url))
                link.Url = "";
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        var plain = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
        renderer.ObjectRenderers.Replace<CodeBlockRenderer>(new CopyableCodeBlockRenderer(plain));
        renderer.Render(document);
        return writer.ToString();
    }

    /// <summary>A code block with a copy button; adminforge.js copies the block's text on click.</summary>
    private sealed class CopyableCodeBlockRenderer : CodeBlockRenderer
    {
        private static readonly string Button =
            "<button type=\"button\" class=\"af-icon-btn\" aria-label=\"Copy\" title=\"Copy\" data-af=\"copy-code\">"
            + $"<svg class=\"af-icon\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\">{Icons.Material.Outlined.ContentCopy}</svg>"
            + "</button>";

        public CopyableCodeBlockRenderer(CodeBlockRenderer? plain)
        {
            if (plain is not null)
                BlocksAsDiv.UnionWith(plain.BlocksAsDiv);
        }

        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            renderer.Write("<div class=\"af-code\">").Write(Button);
            base.Write(renderer, block);
            renderer.WriteLine("</div>");
        }
    }

    // Browsers drop whitespace and control characters inside a scheme, so "java\tscript:" is still javascript.
    /// <summary>A URL on this site: relative, and not "//host", which a browser reads as another host, as it does any backslash.</summary>
    private static bool IsLocal(string? url)
    {
        var compact = new string((url ?? "").Where(c => c > ' ').ToArray()).Replace('\\', '/');
        return Uri.TryCreate(compact, UriKind.Relative, out _) && !compact.StartsWith("//", StringComparison.Ordinal);
    }

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
