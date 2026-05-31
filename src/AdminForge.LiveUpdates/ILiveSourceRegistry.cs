using AdminForge.Core.LiveUpdates;

namespace AdminForge.LiveUpdates;

/// <summary>
/// Process-wide registry of named live data sources. Sources are instantiated lazily
/// the first time they are requested and cached as singletons thereafter, so all UI
/// subscribers share the same underlying fetch/stream loop.
/// </summary>
public interface ILiveSourceRegistry
{
    /// <summary>
    /// Resolve (or create) the live source identified by <paramref name="name"/>. The
    /// caller is responsible for casting the returned object to <see cref="ILiveDataSource{T}"/>
    /// using the type recorded on the originating <see cref="LiveDataSourceMeta"/>.
    /// </summary>
    object GetOrCreate(string name, LiveDataSourceMeta meta);

    /// <summary>
    /// Strongly-typed convenience. Throws when the registry holds a source for
    /// <paramref name="name"/> whose item type doesn't match <typeparamref name="T"/>.
    /// </summary>
    ILiveDataSource<T> GetOrCreate<T>(string name, LiveDataSourceMeta meta);
}
