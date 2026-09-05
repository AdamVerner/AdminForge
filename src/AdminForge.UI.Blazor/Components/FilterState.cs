namespace AdminForge.UI.Blazor.Components;

/// <summary>What the filter bar asks the list to narrow by: per-column values and a search.</summary>
public sealed record FilterState(Dictionary<string, object?> Filters, string? Search);
