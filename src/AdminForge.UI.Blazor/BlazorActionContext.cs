using AdminForge.Core.Contracts;
using AdminForge.UI.Blazor.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace AdminForge.UI.Blazor;

/// <summary>
/// Renderer-bound <see cref="IActionContext"/> for custom-action handlers invoked
/// from the entity view page. Wraps MudBlazor's snackbar/navigation/dialog so the
/// handler stays Core-only while still surfacing toasts and confirmation dialogs.
/// </summary>
public sealed class BlazorActionContext : IActionContext
{
    private readonly ISnackbar _snackbar;
    private readonly NavigationManager _nav;
    private readonly IDialogService _dialogs;
    private readonly Func<Task> _refresh;

    public BlazorActionContext(
        ISnackbar snackbar,
        NavigationManager nav,
        IDialogService dialogs,
        Func<Task> refresh
    )
    {
        ArgumentNullException.ThrowIfNull(snackbar);
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(refresh);
        _snackbar = snackbar;
        _nav = nav;
        _dialogs = dialogs;
        _refresh = refresh;
    }

    public async Task<bool> ConfirmAsync(string message)
    {
        var parameters = new DialogParameters
        {
            ["ContentText"] = message,
            ["ButtonText"] = "Yes",
            ["Color"] = Color.Primary,
        };
        var dialog = await _dialogs
            .ShowAsync<ConfirmDialog>("Confirm", parameters)
            .ConfigureAwait(false);
        var result = await dialog.Result.ConfigureAwait(false);
        return result is { Canceled: false };
    }

    public void ShowSuccess(string message) => _snackbar.Add(message, Severity.Success);

    public void ShowError(string message) => _snackbar.Add(message, Severity.Error);

    public void NavigateTo(string url) => _nav.NavigateTo(url);

    public void Refresh()
    {
        // Fire-and-forget — the entity view page hands us a refresh callback that
        // re-loads the current key. We deliberately don't await: action handlers
        // typically call Refresh() right before returning, and we don't want to
        // block the handler on UI rendering.
        _ = _refresh();
    }
}
