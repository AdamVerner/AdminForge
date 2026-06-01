namespace AdminForge.Core.Configuration;

/// <summary>
/// Return value of a custom create handler registered via
/// <c>EntityBuilder&lt;T&gt;.OnCreate(...)</c>. A discriminated pair: <see cref="Success"/>
/// carries the new entity's identifier (used for navigation and audit), <see cref="Failure"/>
/// carries a human-readable rejection reason that the renderer surfaces inline.
/// </summary>
public abstract record CreateResult
{
    private CreateResult() { }

    /// <summary>The handler persisted the entity successfully. <see cref="Id"/> identifies the new row.</summary>
    public sealed record Success(object Id) : CreateResult;

    /// <summary>The handler rejected the submission. <see cref="Message"/> is shown to the user.</summary>
    public sealed record Failure(string Message) : CreateResult;

    /// <summary>Convenience factory equivalent to <c>new Success(id)</c>.</summary>
    public static CreateResult Ok(object id) => new Success(id);

    /// <summary>Convenience factory equivalent to <c>new Failure(message)</c>.</summary>
    public static CreateResult Error(string message) => new Failure(message);
}
