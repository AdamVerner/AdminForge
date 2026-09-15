using Markdig;

namespace AdminForge.UI.Blazor;

/// <summary>Markdown → HTML for every surface the panel renders it on.</summary>
public static class MarkdownRenderer
{
    // Raw HTML is disabled: a handler may echo user-entered text into its result.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);
}
