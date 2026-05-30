using AdminForge.Core.Metadata;

namespace AdminForge.Core.Contracts;

/// <summary>
/// Sink for audit events fired by AdminForge mutating operations. The host
/// registers an implementation via the fluent <c>.WithAuditLog(...)</c> hook.
/// </summary>
public interface IAuditSink
{
    /// <summary>Called once per audited admin action. Implementations should not throw.</summary>
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
