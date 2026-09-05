namespace AdminForge.Core.Configuration;

/// <summary>What the app bar shows so an operator can tell one deployment from another.</summary>
/// <param name="Color">Any CSS colour; it becomes the app bar background.</param>
public sealed record EnvironmentBadge(string Label, string Color);
