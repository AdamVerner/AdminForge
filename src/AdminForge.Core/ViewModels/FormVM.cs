using AdminForge.Core.Metadata;

namespace AdminForge.Core.ViewModels;

/// <summary>
/// View model for a generic form page. Carries the metadata the renderer needs
/// (title, description, fields) plus a mutable <see cref="Values"/> bag the UI
/// fills as the user types, and a <see cref="Errors"/> map populated after a
/// failed validation pass.
/// </summary>
public sealed class FormVM
{
    public required string RouteName { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<FieldMeta> Fields { get; init; }

    /// <summary>Field name → current value (mutated by the renderer as the user types).</summary>
    public IDictionary<string, object?> Values { get; init; } = new Dictionary<string, object?>();

    /// <summary>Field name → most recent validation error, surfaced inline by the renderer.</summary>
    public IDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Lightweight projection of a registered form used for the sidebar nav (and
/// future React surface). The full <see cref="FormVM"/> is only built when a
/// page is visited.
/// </summary>
public sealed record FormSummary(
    string RouteName,
    string Title,
    AdminForge.Core.Metadata.NavMeta Nav
);
