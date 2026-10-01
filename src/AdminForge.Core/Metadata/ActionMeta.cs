using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;

namespace AdminForge.Core.Metadata;

/// <summary>
/// User-registered custom action attached to an entity. Surfaced as a button row
/// on the entity view page; the handler runs inside a freshly created DI scope.
/// </summary>
public sealed class ActionMeta
{
    /// <summary>Display name, also used as the audit action label.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Async handler invoked when the button fires. Receives the scoped service provider,
    /// the loaded entity instance (boxed — the bridge casts it for typed callers), the
    /// submitted <see cref="Fields"/> and an <see cref="IActionContext"/> wrapping the UI.
    /// </summary>
    public required Func<
        IServiceProvider,
        object,
        FormSubmission,
        IActionContext,
        Task
    > Handler { get; init; }

    /// <summary>Asked of the user in a dialog before the handler runs; empty for a plain button.</summary>
    public List<FieldMeta> Fields { get; } = [];

    /// <summary>
    /// When set, the action context first prompts with this message and aborts on cancel.
    /// </summary>
    public string? ConfirmationMessage { get; set; }

    /// <summary>Optional icon name; renderer maps to its icon set.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Optional colour token (string, mapped by the renderer). Stored as a string so Core
    /// stays free of any UI-framework dependency (MudBlazor in our case).
    /// </summary>
    public string? Color { get; set; }
}
