namespace AdminForge.Core.LiveUpdates;

/// <summary>
/// Discriminator for the merge semantics of a <see cref="LiveUpdate{T}"/>.
/// </summary>
public enum LiveUpdateKind
{
    /// <summary>The payload is the new full dataset; subscribers replace their state.</summary>
    FullReplace,

    /// <summary>The payload contains rows to append (newest first by convention).</summary>
    Append,

    /// <summary>The payload contains rows whose values changed; match by primary key.</summary>
    Update,

    /// <summary>The payload contains rows to remove; match by primary key.</summary>
    Remove,
}
