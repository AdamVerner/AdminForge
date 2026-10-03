namespace AdminForge.Auth;

/// <summary>The panel's own sign-in: its cookie scheme, the policy over it and the paths it serves.</summary>
public static class AdminForgeAuth
{
    public const string Scheme = "AdminForge";
    public const string Policy = "AdminForge.SignedIn";
    public const string Root = "/admin";
    public const string LoginPath = "/admin/login";
    public const string LogoutPath = "/admin/logout";
}
