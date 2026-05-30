using AdminForge.Core.Metadata;

namespace AdminForge.Core.ViewModels;

/// <summary>
/// Phase 1 stub for a generic-form view model. The submit handler dispatch and
/// rendering details land in Phase 4. Captures enough shape (field list +
/// current value bag) for the routing surface to compile.
/// </summary>
public sealed class FormVM
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<FieldMeta> Fields { get; init; }
    public IDictionary<string, object?> Values { get; init; } = new Dictionary<string, object?>();
}
