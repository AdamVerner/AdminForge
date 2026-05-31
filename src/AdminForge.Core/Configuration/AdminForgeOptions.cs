using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Frozen, fully-built configuration handed to the renderer at runtime.
/// Produced by <see cref="AdminForgeBuilder"/>; treat as read-only after the host
/// finishes calling <c>AddAdminForge</c>.
/// </summary>
public sealed class AdminForgeOptions
{
    /// <summary>
    /// URL prefix where the admin panel is mounted (e.g. "/admin").
    /// Leading slash is optional; normalised at mount time.
    /// </summary>
    public string RoutePrefix { get; set; } = "admin";

    /// <summary>Display title shown in the admin shell.</summary>
    public string Title { get; set; } = "Admin";

    /// <summary>
    /// Optional umbrella authorization policy name. When set, every admin
    /// endpoint requires the user to satisfy this policy in addition to any
    /// per-entity, per-action policies. Null = no umbrella (anonymous OK).
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>All registered entities, in registration order.</summary>
    public IReadOnlyList<EntityMeta> Entities { get; init; } = [];

    /// <summary>All registered dashboards, in registration order.</summary>
    public IReadOnlyList<DashboardMeta> Dashboards { get; init; } = [];

    /// <summary>All registered generic forms, in registration order.</summary>
    public IReadOnlyList<FormMeta> Forms { get; init; } = [];

    /// <summary>
    /// Optional audit sink; mutations are recorded here. Null = no auditing.
    /// Wired via <c>AdminForgeBuilder.WithAuditLog(...)</c>.
    /// </summary>
    public IAuditSink? AuditSink { get; init; }

    /// <summary>
    /// Optional visual theming (logo, palette colours). Always non-null;
    /// individual properties on the returned object may be null when unconfigured.
    /// </summary>
    public ThemeOptions Theme { get; init; } = new();
}
