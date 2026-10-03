namespace AdminForge.Core.Metadata;

/// <summary>Where the shell places a nav entry.</summary>
public enum NavArea
{
    /// <summary>The sidebar's main list.</summary>
    Main,

    /// <summary>The panel's own configuration, pinned to the bottom of the sidebar.</summary>
    Settings,

    /// <summary>The signed-in admin's own things, in the account menu beside sign-out.</summary>
    Account,
}
