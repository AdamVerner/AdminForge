namespace AdminForge.Core.ViewModels;

/// <summary>
/// UI-agnostic reference to a related entity. Carries only the primary-key payload
/// (as a string for routing) and a short display label — never the full nested
/// object. This sidesteps cyclic graph serialization and matches the transport
/// shape a future React renderer would consume.
/// </summary>
/// <param name="Key">String-encoded primary key (composite keys: parts joined by "-").</param>
/// <param name="DisplayLabel">Human-readable label resolved via <c>DisplayLabelResolver</c>.</param>
/// <param name="EntityName">Logical name of the related entity (matches <c>EntityMeta.Name</c>).</param>
public sealed record NavigationRef(string Key, string DisplayLabel, string EntityName);

/// <summary>One global-search hit: a row, and the table it belongs to.</summary>
public sealed record SearchHit(string EntityRouteName, string EntityLabel, NavigationRef Row);
