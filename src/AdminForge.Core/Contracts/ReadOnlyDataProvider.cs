namespace AdminForge.Core.Contracts;

/// <summary>
/// Base for a provider that only lists and shows. Pair it with <c>EntityBuilder.ReadOnly()</c>,
/// which hides the buttons; the mutators here refuse should anything reach them anyway.
/// </summary>
public abstract class ReadOnlyDataProvider<T> : IAdminDataProvider<T>
    where T : class
{
    public abstract Task<ListResult<T>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    );

    public abstract Task<T?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    );

    public Task<T> CreateAsync(T entity, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    public Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    public Task<bool> DeleteAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new($"'{typeof(T).Name}' is read-only in the admin panel.");
}
