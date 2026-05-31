namespace AdminForge.Core.LiveUpdates;

/// <summary>
/// Envelope carried over <see cref="ILiveDataSource{T}"/> describing one push to subscribers.
/// </summary>
/// <remarks>
/// Semantics keyed by <see cref="Kind"/>:
/// <list type="bullet">
///   <item><see cref="LiveUpdateKind.FullReplace"/> — <see cref="Items"/> is the new full set.</item>
///   <item><see cref="LiveUpdateKind.Append"/> — append <see cref="Items"/> to the head of the existing set.</item>
///   <item><see cref="LiveUpdateKind.Update"/> — replace rows in the existing set, matched by primary key.</item>
///   <item><see cref="LiveUpdateKind.Remove"/> — drop rows from the existing set, matched by primary key.</item>
/// </list>
/// </remarks>
public sealed record LiveUpdate<T>(
    LiveUpdateKind Kind,
    IReadOnlyList<T> Items,
    DateTimeOffset Timestamp
)
{
    public static LiveUpdate<T> FullReplace(IReadOnlyList<T> items, DateTimeOffset timestamp) =>
        new(LiveUpdateKind.FullReplace, items, timestamp);

    public static LiveUpdate<T> Append(IReadOnlyList<T> items, DateTimeOffset timestamp) =>
        new(LiveUpdateKind.Append, items, timestamp);

    public static LiveUpdate<T> Update(IReadOnlyList<T> items, DateTimeOffset timestamp) =>
        new(LiveUpdateKind.Update, items, timestamp);

    public static LiveUpdate<T> Remove(IReadOnlyList<T> items, DateTimeOffset timestamp) =>
        new(LiveUpdateKind.Remove, items, timestamp);
}
