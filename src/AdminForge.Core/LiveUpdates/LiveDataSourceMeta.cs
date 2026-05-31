namespace AdminForge.Core.LiveUpdates;

/// <summary>
/// Renderer-agnostic descriptor of a streaming live data source registered on a
/// dashboard line chart. The concrete <see cref="ILiveDataSource{T}"/> implementation
/// (in <c>AdminForge.LiveUpdates</c>) materialises from this meta via
/// <c>ILiveSourceRegistry</c>.
/// </summary>
/// <remarks>
/// After the Phase 5 scope narrowing only the streaming path remains: polling is now
/// done at the page level by a simple <c>Task.Delay</c> loop, re-invoking the existing
/// fetch/data delegate. <see cref="Payload"/> is the user-supplied
/// <see cref="IAsyncEnumerable{T}"/>, boxed for storage on POCO metadata; cast to
/// <c>IAsyncEnumerable&lt;TItem&gt;</c> at materialisation time.
/// </remarks>
public sealed class LiveDataSourceMeta
{
    /// <summary>CLR type of the item emitted by the stream — used to validate registry lookups.</summary>
    public required Type ItemType { get; init; }

    /// <summary>The user-supplied <c>IAsyncEnumerable&lt;TItem&gt;</c>, boxed as <see cref="object"/>.</summary>
    public required object Payload { get; init; }
}
