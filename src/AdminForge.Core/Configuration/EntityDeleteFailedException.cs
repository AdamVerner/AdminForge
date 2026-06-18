namespace AdminForge.Core.Configuration;

/// <summary>
/// Thrown by the bridge when a custom delete handler returns
/// <see cref="DeleteResult.Failure"/>. The renderer catches this and shows the
/// message as an error (distinct from arbitrary data-layer failures so the UI can
/// distinguish business-logic rejection from a real error).
/// </summary>
public sealed class EntityDeleteFailedException : Exception
{
    public EntityDeleteFailedException(string entityName, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentNullException.ThrowIfNull(message);
        EntityName = entityName;
    }

    public string EntityName { get; }
}
