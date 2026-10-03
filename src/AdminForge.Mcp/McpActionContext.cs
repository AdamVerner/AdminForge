using System.Text;
using AdminForge.Core.Contracts;

namespace AdminForge.Mcp;

/// <summary>Collects what a handler would have shown; a confirmation is answered yes, a refresh ignored.</summary>
internal sealed class McpActionContext : IActionContext
{
    private readonly StringBuilder _text = new();

    public bool Failed { get; private set; }

    public string Text => _text.ToString().TrimEnd();

    public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);

    public void ShowSuccess(string message) => _text.AppendLine(message);

    public void ShowError(string message)
    {
        Failed = true;
        _text.AppendLine(message);
    }

    public void ShowResult(string markdown) => _text.AppendLine(markdown);

    public void NavigateTo(string url) => _text.AppendLine($"See {url}");

    public void Refresh() { }
}
