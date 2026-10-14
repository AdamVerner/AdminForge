using Microsoft.AspNetCore.Components;

namespace AdminForge.UI;

/// <summary>Answers a page's address as not found, with the page's own message on the router's not-found page.</summary>
internal sealed class Missing(NavigationManager nav)
{
    private string? _uri;
    private (string Body, string? Href, string? Back) _note;

    /// <summary>The message for the current address, or null when the router itself found nothing.</summary>
    public (string Body, string? Href, string? Back)? Note => _uri == nav.Uri ? _note : null;

    public void Show(string body, string? href = null, string? back = null)
    {
        _uri = nav.Uri;
        _note = (body, href, back);
        nav.NotFound();
    }
}
