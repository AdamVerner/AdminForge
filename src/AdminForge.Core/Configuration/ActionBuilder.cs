using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for configuring a custom entity action added via
/// <c>EntityBuilder.AddAction(...)</c>.
/// </summary>
public sealed class ActionBuilder
{
    private readonly ActionMeta _meta;

    internal ActionBuilder(ActionMeta meta) => _meta = meta;

    /// <summary>
    /// Prompt the user with <paramref name="message"/> before invoking the handler.
    /// The handler is only called when the user confirms.
    /// </summary>
    public ActionBuilder RequireConfirmation(string message = "Are you sure?")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _meta.ConfirmationMessage = message;
        return this;
    }

    /// <summary>Icon name (renderer-defined token, e.g. MudBlazor's <c>Icons.Material.Filled.Email</c>).</summary>
    public ActionBuilder Icon(string icon)
    {
        _meta.Icon = icon;
        return this;
    }

    /// <summary>
    /// Colour token (string). The renderer maps this to its own colour system —
    /// for the MudBlazor UI, valid tokens are <c>Primary</c>, <c>Secondary</c>,
    /// <c>Success</c>, <c>Warning</c>, <c>Error</c>, etc.
    /// </summary>
    public ActionBuilder Color(string color)
    {
        _meta.Color = color;
        return this;
    }
}
