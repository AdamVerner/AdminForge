using System.Collections.Concurrent;
using System.Reflection;
using AdminForge.Core.LiveUpdates;

namespace AdminForge.LiveUpdates;

/// <summary>
/// Default <see cref="ILiveSourceRegistry"/> implementation. Backed by a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>; sources are materialised once
/// per name via reflective dispatch over the user-supplied streaming source carried
/// on <see cref="LiveDataSourceMeta"/>.
/// </summary>
/// <remarks>
/// Phase 5 was narrowed: only <see cref="StreamingDataSource{T}"/> remains. Polling is
/// now done at the page level (one <c>Task.Delay</c> loop per browser tab) so the
/// registry-backed multicast scaffolding is exclusively used to share a single
/// upstream <see cref="IAsyncEnumerable{T}"/> across multiple subscribed circuits.
/// </remarks>
public sealed class LiveSourceRegistry : ILiveSourceRegistry, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<string, object> _sources = new(StringComparer.Ordinal);

    public LiveSourceRegistry(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public object GetOrCreate(string name, LiveDataSourceMeta meta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(meta);
        return _sources.GetOrAdd(name, _ => Materialise(meta));
    }

    public ILiveDataSource<T> GetOrCreate<T>(string name, LiveDataSourceMeta meta)
    {
        var raw = GetOrCreate(name, meta);
        if (raw is not ILiveDataSource<T> typed)
        {
            throw new InvalidOperationException(
                $"Live source '{name}' is registered for item type '{meta.ItemType.Name}' "
                    + $"but was requested as '{typeof(T).Name}'."
            );
        }
        return typed;
    }

    private object Materialise(LiveDataSourceMeta meta)
    {
        var method = typeof(LiveSourceRegistry)
            .GetMethod(nameof(MaterialiseGeneric), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(meta.ItemType);
        return method.Invoke(this, new object[] { meta })!;
    }

    private object MaterialiseGeneric<T>(LiveDataSourceMeta meta)
    {
        // services is reserved for future TimeProvider injection; pulled from DI lazily.
        var timeProvider = (TimeProvider?)_services.GetService(typeof(TimeProvider));
        var stream = (IAsyncEnumerable<T>)meta.Payload;
        return new StreamingDataSource<T>(stream, timeProvider);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources.Values)
        {
            if (source is IAsyncDisposable iad)
                await iad.DisposeAsync().ConfigureAwait(false);
            else if (source is IDisposable id)
                id.Dispose();
        }
        _sources.Clear();
    }
}
