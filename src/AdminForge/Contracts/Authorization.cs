using System.Security.Claims;

namespace AdminForge;

/// <summary>What a table operation does to its rows.</summary>
public enum AdminOp
{
    Read,
    Create,
    Update,
    Delete,
}

/// <summary>One question to the policy. The set is closed: a table operation, an action, a form, a dashboard or a host page.</summary>
public abstract record AdminRequest
{
    private protected AdminRequest(ClaimsPrincipal user) => User = user;

    public ClaimsPrincipal User { get; }
}

/// <summary>A table operation, with the stored row whenever the operation has one.</summary>
/// <remarks><see cref="Row"/> is null for a list, a create, and when the panel only asks whether to offer the operation at all.</remarks>
public sealed record TableRequest<T>(ClaimsPrincipal User, string TableId, AdminOp Op, T? Row)
    : AdminRequest(User)
    where T : class;

/// <summary>An action on a row. <see cref="Row"/> is null only when the panel asks whether to offer the action at all.</summary>
public sealed record ActionRequest<T>(ClaimsPrincipal User, string TableId, string ActionId, T? Row)
    : AdminRequest(User)
    where T : class;

/// <summary>A form's submission or its choices.</summary>
public sealed record FormRequest(ClaimsPrincipal User, string FormId) : AdminRequest(User);

/// <summary>A dashboard and every widget on it.</summary>
public sealed record DashboardRequest(ClaimsPrincipal User, string DashboardId)
    : AdminRequest(User);

/// <summary>A host page registered with <c>Page&lt;T&gt;</c>: whether the sidebar lists it and whether it renders.</summary>
public sealed record HostPageRequest(ClaimsPrincipal User, string PageId) : AdminRequest(User);

/// <summary>The host's per-operation decision, asked after the panel's umbrella policy allowed the caller.</summary>
/// <remarks>
/// A request is a <see cref="TableRequest{T}"/>, <see cref="ActionRequest{T}"/>, <see cref="FormRequest"/>,
/// <see cref="DashboardRequest"/> or <see cref="HostPageRequest"/>. A list or a search asks Read with a null row,
/// so a rule about the row never filters them: the source must leave out rows the caller may not read.
/// </remarks>
public interface IAdminPolicy
{
    ValueTask<bool> AllowsAsync(AdminRequest request, CancellationToken ct);
}

/// <summary>Who the current operation runs as: the circuit's user in the panel, the connection's admin over MCP.</summary>
public interface IUserAccessor
{
    ClaimsPrincipal GetUser();

    /// <summary>The <c>sub</c> or name-identifier claim, else the identity's name; null when anonymous.</summary>
    string? GetUserId();
}
