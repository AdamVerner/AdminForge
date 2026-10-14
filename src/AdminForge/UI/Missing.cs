using Microsoft.AspNetCore.Components;

namespace AdminForge.UI;

/// <summary>Answers a page's address as not found, with the page's own message on the router's not-found page.</summary>
internal sealed class Missing(NavigationManager nav)
{
    private string? _uri;
    private (string Body, string? Href, string? Back) _note;

    /// <summary>The message for the current address, or null when the router itself found nothing.</summary>
    public (string Body, string? Href, string? Back)? Note => _uri == nav.Uri ? _note : null;

    public void Table(string id) => Show($"There is no table '{id}'.");

    public void Key(TableModel table, string key) =>
        Show($"'{key}' is not a valid key for {table.Title}.", table);

    public void Row(TableModel table, string key) =>
        Show($"{table.Title} has no row with the key '{key}'.", table);

    /// <summary>Not found, with a way back to <paramref name="back"/>'s list when it is given.</summary>
    public void Show(string body, TableModel? back = null)
    {
        _uri = nav.Uri;
        _note = (body, back is null ? null : Links.List(back.Id), back is null ? null : $"Back to {back.Title}");
        nav.NotFound();
    }
}
