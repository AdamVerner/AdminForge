namespace AdminForge.Core.Configuration;

/// <summary>
/// Return value of a custom update handler registered via
/// <c>EntityBuilder&lt;T&gt;.OnUpdate(...)</c>. A discriminated pair: <see cref="Success"/>
/// is parameterless (the row's identity hasn't changed, so there's nothing to return),
/// <see cref="Failure"/> carries a human-readable rejection reason that the renderer
/// surfaces inline. Mirrors <see cref="CreateResult"/>.
/// </summary>
public abstract record UpdateResult
{
    private UpdateResult() { }

    /// <summary>The handler persisted the update successfully.</summary>
    public sealed record Success : UpdateResult;

    /// <summary>The handler rejected the submission. <see cref="Message"/> is shown to the user.</summary>
    public sealed record Failure(string Message) : UpdateResult;

    /// <summary>Convenience factory equivalent to <c>new Success()</c>.</summary>
    public static UpdateResult Ok() => new Success();

    /// <summary>Convenience factory equivalent to <c>new Failure(message)</c>.</summary>
    public static UpdateResult Error(string message) => new Failure(message);
}
