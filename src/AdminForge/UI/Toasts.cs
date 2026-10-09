using MudBlazor;

namespace AdminForge.UI;

/// <summary>Toasts: success goes away by itself, an error stays until it is closed.</summary>
internal static class Toasts
{
    public static void Success(ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Success);

    public static void Error(ISnackbar snackbar, string message) =>
        snackbar.Add(message, Severity.Error, o => o.RequireInteraction = true);
}
