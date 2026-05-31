namespace AdminForge.Core.Contracts;

/// <summary>
/// Renderer-agnostic surface handed to custom-action handlers. Lets a Core-only
/// handler interact with the user (confirmation prompt, toast, redirect) without
/// referencing the UI framework.
/// </summary>
public interface IActionContext
{
    /// <summary>
    /// Prompts the user to confirm with <paramref name="message"/>. Returns true on
    /// confirm, false on cancel/dismiss. Handlers typically short-circuit on false.
    /// </summary>
    Task<bool> ConfirmAsync(string message);

    /// <summary>Surfaces a success message in the renderer's notification surface.</summary>
    void ShowSuccess(string message);

    /// <summary>Surfaces an error message in the renderer's notification surface.</summary>
    void ShowError(string message);

    /// <summary>Navigates the user to an absolute or app-relative URL.</summary>
    void NavigateTo(string url);

    /// <summary>Requests the current page to reload its data after the action settles.</summary>
    void Refresh();
}
