using AdminForge.Core.Contracts;

namespace TodoApp;

/// <summary>
/// Non-EF entity: a singleton set of in-memory settings, registered with
/// <c>AddTable&lt;SiteSettings&gt;</c> and served by a custom data provider.
/// </summary>
public sealed class SiteSettings
{
    public int Id { get; set; } = 1;
    public bool MaintenanceMode { get; set; }
    public int MaxItemsPerPage { get; set; } = 25;
    public string WelcomeMessage { get; set; } = "Welcome to Todo Admin!";
}

/// <summary>Singleton in-memory backing store for <see cref="SiteSettings"/>.</summary>
public sealed class SiteSettingsStore
{
    private SiteSettings _current = new();

    public SiteSettings Get() =>
        new()
        {
            Id = _current.Id,
            MaintenanceMode = _current.MaintenanceMode,
            MaxItemsPerPage = _current.MaxItemsPerPage,
            WelcomeMessage = _current.WelcomeMessage,
        };

    public void Set(SiteSettings settings) => _current = settings;
}

/// <summary>
/// Custom <see cref="IAdminDataProvider{T}"/> for <see cref="SiteSettings"/>.
/// Reads and writes the <see cref="SiteSettingsStore"/> singleton; there is always
/// exactly one row (Id = 1). Delete is a no-op that returns false.
/// </summary>
public sealed class SiteSettingsDataProvider(SiteSettingsStore store)
    : IAdminDataProvider<SiteSettings>
{
    public Task<ListResult<SiteSettings>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new ListResult<SiteSettings> { Items = [store.Get()], TotalCount = 1 });

    public Task<SiteSettings?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<SiteSettings?>(keyValues is [1 or "1"] ? store.Get() : null);

    public Task<SiteSettings> CreateAsync(
        SiteSettings entity,
        CancellationToken cancellationToken = default
    )
    {
        entity.Id = 1;
        store.Set(entity);
        return Task.FromResult(store.Get());
    }

    public Task<SiteSettings> UpdateAsync(
        SiteSettings entity,
        CancellationToken cancellationToken = default
    )
    {
        entity.Id = 1;
        store.Set(entity);
        return Task.FromResult(store.Get());
    }

    public Task<bool> DeleteAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("SiteSettings cannot be deleted.");
}
