namespace AdminForge.Core.Metadata;

/// <summary>
/// Sidebar navigation entry for an entity, dashboard, or form.
/// </summary>
public sealed class NavMeta
{
    /// <summary>Display label. Null means: fall back to the owner's <c>Label</c>.</summary>
    public string? Label { get; set; }

    /// <summary>Optional collapsible group; entries sharing a group name nest under it.</summary>
    public string? Group { get; set; }

    /// <summary>Sort order within a group (lower comes first). Null defers to registration order.</summary>
    public int? Order { get; set; }

    /// <summary>Hide this entry from the sidebar entirely.</summary>
    public bool Hidden { get; set; }
}
