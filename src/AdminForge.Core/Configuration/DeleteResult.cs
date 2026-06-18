namespace AdminForge.Core.Configuration;

/// <summary>
/// Return value of a custom delete handler registered via
/// <c>EntityBuilder&lt;T&gt;.OnDelete(...)</c>. A discriminated pair: <see cref="Success"/>
/// is parameterless (the row is gone), <see cref="Failure"/> carries a human-readable
/// rejection reason that the renderer surfaces as an error. Mirrors <see cref="UpdateResult"/>.
/// </summary>
public abstract record DeleteResult
{
    private DeleteResult() { }

    /// <summary>The handler deleted the entity successfully.</summary>
    public sealed record Success : DeleteResult;

    /// <summary>The handler rejected the deletion. <see cref="Message"/> is shown to the user.</summary>
    public sealed record Failure(string Message) : DeleteResult;

    /// <summary>Convenience factory equivalent to <c>new Success()</c>.</summary>
    public static DeleteResult Ok() => new Success();

    /// <summary>Convenience factory equivalent to <c>new Failure(message)</c>.</summary>
    public static DeleteResult Error(string message) => new Failure(message);
}
