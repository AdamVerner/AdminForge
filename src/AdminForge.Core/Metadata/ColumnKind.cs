namespace AdminForge.Core.Metadata;

/// <summary>
/// Classification of a column emitted by the reflection scanner.
/// Drives both how a value is rendered and how it is edited.
/// </summary>
public enum ColumnKind
{
    /// <summary>Scalar value (string, number, bool, DateTime, Guid, byte[], etc.).</summary>
    Scalar,

    /// <summary>CLR enum.</summary>
    Enum,

    /// <summary>Reference navigation to another entity (single).</summary>
    NavigationReference,

    /// <summary>Collection navigation to another entity (many).</summary>
    NavigationCollection,

    /// <summary>Owned/complex type (rendered inline).</summary>
    Owned,
}
