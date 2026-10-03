namespace AdminForge.Core.Configuration;

/// <summary>
/// What a package adds to the panel, applied after the host's own configuration. Register as a
/// singleton; a package that owns a table registers its provider beside it.
/// </summary>
public sealed record AdminForgeContribution(Action<AdminForgeBuilder> Configure);
