namespace AdminForge;

/// <summary>
/// Phase 0 options surface. Will grow in Phase 1+ (auth policy name, audit sink, theme, etc).
/// </summary>
public sealed class AdminForgeOptions
{
    /// <summary>
    /// URL prefix where the admin panel is mounted (e.g. "/admin").
    /// Leading slash is optional; normalised at mount time.
    /// </summary>
    public string RoutePrefix { get; set; } = "admin";

    /// <summary>
    /// Display title shown in the admin shell.
    /// </summary>
    public string Title { get; set; } = "Admin";
}
