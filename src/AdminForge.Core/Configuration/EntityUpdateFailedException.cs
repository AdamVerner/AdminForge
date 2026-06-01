namespace AdminForge.Core.Configuration;

/// <summary>
/// Thrown by the bridge when a custom update handler returns
/// <see cref="UpdateResult.Failure"/>. The renderer catches this and shows the
/// message inline (distinct from arbitrary data-layer failures so the UI can
/// distinguish business-logic rejection from a real error). Mirrors
/// <see cref="EntityCreateFailedException"/>.
/// </summary>
public sealed class EntityUpdateFailedException : Exception
{
    public EntityUpdateFailedException(string entityName, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentNullException.ThrowIfNull(message);
        EntityName = entityName;
    }

    public string EntityName { get; }
}
