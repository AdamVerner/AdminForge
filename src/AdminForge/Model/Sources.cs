using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>A table's source with its type erased. Every delegate takes the operation's services.</summary>
internal sealed record SourceModel
{
    public required Func<
        IServiceProvider,
        ListRequest,
        CancellationToken,
        Task<(IReadOnlyList<object> Rows, int Total)>
    > List { get; init; }

    /// <summary>Takes the decoded key parts.</summary>
    public Func<IServiceProvider, object?[], CancellationToken, Task<object?>>? Find { get; init; }
    public Func<IServiceProvider, object, CancellationToken, Task<object>>? Create { get; init; }
    public Func<
        IServiceProvider,
        object,
        IReadOnlySet<string>,
        CancellationToken,
        Task<object>
    >? Update { get; init; }
    public Func<IServiceProvider, object, CancellationToken, Task>? Delete { get; init; }

    /// <summary>The key type the source's find takes; <c>object?[]</c> takes the parts as they are.</summary>
    public Type? KeyType { get; init; }
    public bool Search { get; init; }
    public bool FindsKey { get; init; }

    /// <summary>Null when the source filters or sorts on any column.</summary>
    public IReadOnlySet<string>? Filters { get; init; }
    public IReadOnlySet<string>? Sorts { get; init; }

    public bool CanFilter(string column) => Filters?.Contains(column) ?? true;

    public bool CanSort(string column) => Sorts?.Contains(column) ?? true;
}

/// <summary>What a source honours: search, and the columns it filters and sorts on.</summary>
public sealed class SourceOptions<T>
    where T : class
{
    internal bool SearchOn { get; private set; }
    internal bool FindsKey { get; private set; }
    internal HashSet<string> FilterColumns { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> SortColumns { get; } = new(StringComparer.Ordinal);

    /// <summary>The source honours <see cref="AdminQuery{T}.Search"/>. With <paramref name="findsKey"/>, a pasted key opens that row.</summary>
    public SourceOptions<T> Search(bool findsKey = false)
    {
        SearchOn = true;
        FindsKey = findsKey;
        return this;
    }

    public SourceOptions<T> Filters(params Expression<Func<T, object?>>[] columns)
    {
        foreach (var c in columns)
            FilterColumns.Add(Members.Name(c));
        return this;
    }

    public SourceOptions<T> Sorts(params Expression<Func<T, object?>>[] columns)
    {
        foreach (var c in columns)
            SortColumns.Add(Members.Name(c));
        return this;
    }
}

/// <summary>A source class that searches, filters and sorts on any column, so it declares none of them.</summary>
internal interface IQueriesAnything;

internal static class Sources
{
    public static AdminQuery<T> Query<T>(ListRequest q)
        where T : class =>
        new(new(q.Page, q.PageSize), q.Search, q.SortBy, q.Descending, q.Filters, q.Exact, q.Hidden);

    public static SourceModel Inline<T, TService>(
        Func<TService, AdminQuery<T>, CancellationToken, Task<AdminPage<T>>> list,
        SourceOptions<T> options
    )
        where T : class
        where TService : class =>
        Declared(
            options,
            new SourceModel
            {
                List = async (sp, q, ct) =>
                {
                    var page = await list(Resolve<TService>(sp), Query<T>(q), ct)
                        .ConfigureAwait(false);
                    return (page.Rows, page.Total);
                },
            }
        );

    public static SourceModel Inline<T, TService, TKey>(
        Func<TService, AdminQuery<T>, CancellationToken, Task<AdminPage<T>>> list,
        Func<TService, TKey, CancellationToken, Task<T?>> find,
        SourceOptions<T> options
    )
        where T : class
        where TService : class =>
        Inline(list, options) with
        {
            Find = async (sp, key, ct) =>
                await find(Resolve<TService>(sp), Key<TKey>(key), ct).ConfigureAwait(false),
            KeyType = typeof(TKey),
        };

    /// <summary>A source class: list always, find, writes and delete as far as it implements them.</summary>
    public static SourceModel Class<T, TSource>(SourceOptions<T>? options)
        where T : class
        where TSource : class
    {
        if (!typeof(IAdminSource<T>).IsAssignableFrom(typeof(TSource)))
            throw new InvalidOperationException(
                $"{typeof(TSource).Name} does not implement IAdminSource<{typeof(T).Name}>."
            );
        var source = new SourceModel
        {
            List = async (sp, q, ct) =>
            {
                var page = await ((IAdminSource<T>)Resolve<TSource>(sp))
                    .ListAsync(Query<T>(q), ct)
                    .ConfigureAwait(false);
                return (page.Rows, page.Total);
            },
        };
        // A source that queries any column keeps that with options; they turn on finding by key or narrow it.
        source = typeof(IQueriesAnything).IsAssignableFrom(typeof(TSource))
            ? source with
            {
                Search = true,
                FindsKey = options?.FindsKey == true,
                Filters = options is { FilterColumns.Count: > 0 } ? options.FilterColumns : null,
                Sorts = options is { SortColumns.Count: > 0 } ? options.SortColumns : null,
            }
            : Declared(options ?? new(), source);

        var finder = typeof(TSource)
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IAdminSource<,>)
                && i.GenericTypeArguments[0] == typeof(T)
            );
        if (finder is not null)
            source = (SourceModel)
                typeof(Sources)
                    .GetMethod(nameof(WithFind), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(typeof(T), typeof(TSource), finder.GenericTypeArguments[1])
                    .Invoke(null, [source])!;

        if (typeof(IAdminWritable<T>).IsAssignableFrom(typeof(TSource)))
            source = source with
            {
                Create = async (sp, row, ct) =>
                    await ((IAdminWritable<T>)Resolve<TSource>(sp))
                        .CreateAsync((T)row, ct)
                        .ConfigureAwait(false),
                Update = async (sp, row, changed, ct) =>
                    await ((IAdminWritable<T>)Resolve<TSource>(sp))
                        .UpdateAsync((T)row, changed, ct)
                        .ConfigureAwait(false),
            };
        if (typeof(IAdminDeletable<T>).IsAssignableFrom(typeof(TSource)))
            source = source with
            {
                Delete = (sp, row, ct) =>
                    ((IAdminDeletable<T>)Resolve<TSource>(sp)).DeleteAsync((T)row, ct),
            };
        return source;
    }

    private static SourceModel WithFind<T, TSource, TKey>(SourceModel source)
        where T : class
        where TSource : class =>
        source with
        {
            Find = async (sp, key, ct) =>
                await ((IAdminSource<T, TKey>)Resolve<TSource>(sp))
                    .FindAsync(Key<TKey>(key), ct)
                    .ConfigureAwait(false),
            KeyType = typeof(TKey),
        };

    private static SourceModel Declared<T>(SourceOptions<T> options, SourceModel source)
        where T : class =>
        source with
        {
            Search = options.SearchOn,
            FindsKey = options.FindsKey,
            Filters = options.FilterColumns,
            Sorts = options.SortColumns,
        };

    // A host need not register a source or service: one is built from the scope when it is not.
    private static TService Resolve<TService>(IServiceProvider sp)
        where TService : class => ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp);

    private static TKey Key<TKey>(object?[] parts) =>
        typeof(TKey) == typeof(object?[]) ? (TKey)(object)parts : (TKey)parts[0]!;
}
