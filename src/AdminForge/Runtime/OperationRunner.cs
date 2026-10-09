using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdminForge;

/// <summary>Refused by the umbrella policy, a table rule or the host's policy.</summary>
internal sealed class AdminForbiddenException(string what) : AdminRejection($"You may not {what}.");

/// <summary>Runs every panel operation; no other code opens its scope, asks the policy or writes the audit.</summary>
internal sealed class OperationRunner(
    IServiceScopeFactory scopes,
    IUserAccessor caller,
    IAuthorizationService authorization,
    PanelModel panel,
    ILogger<OperationRunner> log
)
{
    public async Task<T> RunAsync<T>(
        string what,
        Func<Operation, Task<T>> body,
        CancellationToken ct
    )
    {
        var (scope, op) = await OpenAsync(ct).ConfigureAwait(false);
        await using (scope)
        {
            try
            {
                return await body(op).ConfigureAwait(false);
            }
            catch (Exception ex) when (Unexpected(ex, ct))
            {
                throw Fail(ex, what, "failed");
            }
        }
    }

    /// <summary>Authorizes when enumeration starts and keeps the scope open until it ends; each enumeration is its own.</summary>
    public async IAsyncEnumerable<T> StreamAsync<T>(
        string what,
        Func<Operation, Task<IAsyncEnumerable<T>>> open,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var (scope, op) = await OpenAsync(ct).ConfigureAwait(false);
        await using (scope)
        {
            IAsyncEnumerator<T> items;
            try
            {
                items = (await open(op).ConfigureAwait(false)).GetAsyncEnumerator(ct);
            }
            catch (Exception ex) when (Unexpected(ex, ct))
            {
                throw Fail(ex, what, "failed");
            }
            await using (items)
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        more = await items.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (Unexpected(ex, ct))
                    {
                        throw Fail(ex, what, "stopped");
                    }
                    if (!more)
                        yield break;
                    yield return items.Current;
                }
            }
        }
    }

    private async Task<(AsyncServiceScope, Operation)> OpenAsync(CancellationToken ct)
    {
        var user = caller.GetUser();
        if (
            panel.Policy is { } policy
            && !(await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false)).Succeeded
        )
            throw new AdminForbiddenException("use this panel");
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OperationUserAccessor>().User = user;
        return (scope, new Operation(scope.ServiceProvider, user, log, ct));
    }

    // A cancellation nobody asked for, such as an HttpClient timeout, is a failure like any other.
    private static bool Unexpected(Exception ex, CancellationToken ct) =>
        ex is not AdminRejection
        && !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private AdminRejection Fail(Exception ex, string what, string verb)
    {
        log.LogError(ex, "AdminForge: {Operation} {Verb}.", what, verb);
        return new AdminRejection($"{what} {verb}. The error is in the server log.");
    }
}

/// <summary>One running operation: its scope's services, its caller, and the checks and audit it may make.</summary>
internal sealed class Operation(
    IServiceProvider services,
    ClaimsPrincipal user,
    ILogger log,
    CancellationToken ct
)
{
    public IServiceProvider Services { get; } = services;
    public ClaimsPrincipal User { get; } = user;
    public CancellationToken CancellationToken { get; } = ct;

    public async Task<bool> AllowsAsync(AdminRequest request, bool rule = true) =>
        rule
        && (
            Services.GetService<IAdminPolicy>() is not { } policy
            || await policy.AllowsAsync(request, CancellationToken).ConfigureAwait(false)
        );

    public Task<bool> AllowsAsync(TableModel table, AdminOp op, object? row) =>
        AllowsAsync(table.Request(User, op, row), table.Authorize?.Invoke(User, op, row) ?? true);

    public Task<bool> AllowsAsync(TableModel table, ActionModel action, object? row) =>
        AllowsAsync(
            table.ActionRequest(User, action.Id, row),
            table.Authorize?.Invoke(User, AdminOp.Update, row) ?? true
        );

    public async Task EnsureAsync(Task<bool> allowed, string what)
    {
        if (!await allowed.ConfigureAwait(false))
            throw new AdminForbiddenException(what);
    }

    public async Task AuditAsync(
        AdminSurface surface,
        AdminAuditOperation kind,
        string? table,
        string? key,
        IReadOnlyDictionary<string, AdminChange>? changes = null,
        string? action = null,
        IReadOnlyDictionary<string, object?>? input = null
    )
    {
        if (Services.GetService<IAdminAuditSink>() is not { } sink)
            return;
        // The write is already stored: a failed record must not report it failed and invite a retry.
        try
        {
            await sink.RecordAsync(
                    new AdminAuditEvent(
                        kind,
                        table,
                        key,
                        action,
                        input ?? new Dictionary<string, object?>(),
                        changes ?? new Dictionary<string, AdminChange>(),
                        User,
                        surface,
                        DateTimeOffset.UtcNow
                    ),
                    CancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
            when (!(ex is OperationCanceledException && CancellationToken.IsCancellationRequested))
        {
            log.LogError(ex, "AdminForge: recording {Operation} on {Table} failed.", kind, table);
        }
    }
}
