namespace AdminForge.Core.Configuration;

/// <summary>
/// Thrown by the bridge when a custom create handler returns
/// <see cref="CreateResult.Failure"/>. The renderer catches this and shows the
/// message inline (distinct from arbitrary data-layer failures so the UI can
/// distinguish business-logic rejection from a real error).
/// </summary>
public sealed class EntityCreateFailedException : Exception
{
    public EntityCreateFailedException(string entityName, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentNullException.ThrowIfNull(message);
        EntityName = entityName;
    }

    public string EntityName { get; }
}
