using AdminForge.UI.Blazor;

namespace AdminForge.UnitTests.UI;

public class MarkdownRendererTests
{
    [Fact]
    public void Renders_Tables_And_Escapes_Raw_Html()
    {
        var html = MarkdownRenderer.ToHtml(
            """
            | a | b |
            |---|---|
            | 1 | <script>x()</script> |
            """
        );
        Assert.Contains("<table>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }
}
