using AdminForge.Core.Contracts;

namespace CrmApp;

/// <summary>Organizations: searched, sorted and filtered by the store.</summary>
public sealed class OrganizationProvider(CrmStore store) : ReadOnlyDataProvider<Organization>
{
    public override Task<ListResult<Organization>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(InMemoryQuery.Apply(store.Organizations, query));

    public override Task<Organization?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(store.Organizations.FirstOrDefault(o => Key.Is(o.Id, keyValues)));
}

public sealed class AccountProvider(CrmStore store) : ReadOnlyDataProvider<Account>
{
    public override Task<ListResult<Account>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(InMemoryQuery.Apply(store.Accounts, query));

    public override Task<Account?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(store.Accounts.FirstOrDefault(a => Key.Is(a.Id, keyValues)));
}

public sealed class MemberProvider(CrmStore store) : ReadOnlyDataProvider<Member>
{
    public override Task<ListResult<Member>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(InMemoryQuery.Apply(store.Members, query));

    public override Task<Member?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(store.Members.FirstOrDefault(m => Key.Is(m.Id, keyValues)));
}

/// <summary>API keys: the service behind this one has no free-text search, so the table has no box.</summary>
public sealed class ApiKeyProvider(CrmStore store) : ReadOnlyDataProvider<ApiKey>
{
    public override Task<ListResult<ApiKey>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(InMemoryQuery.Apply(store.ApiKeys, query, searchable: false));

    public override Task<ApiKey?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(store.ApiKeys.FirstOrDefault(k => Key.Is(k.Id, keyValues)));
}

/// <summary>A key arrives as the route segment carried it — an int from a link, a string from a URL.</summary>
internal static class Key
{
    public static bool Is(int id, object?[] keyValues) =>
        keyValues is [{ } value] && (value is int i ? i == id : value.ToString() == id.ToString());
}
