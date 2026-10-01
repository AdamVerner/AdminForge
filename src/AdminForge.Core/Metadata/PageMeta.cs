namespace AdminForge.Core.Metadata;

/// <summary>A page the host wrote itself, routed by its own route attribute and listed in the sidebar.</summary>
public sealed class PageMeta
{
    /// <summary>The Razor component. Its route must sit under the panel's prefix.</summary>
    public required Type ComponentType { get; init; }

    public NavMeta Nav { get; } = new();
}
