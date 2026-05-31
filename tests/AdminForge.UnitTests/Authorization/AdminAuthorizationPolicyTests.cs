using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;

namespace AdminForge.UnitTests.Authorization;

public class AdminAuthorizationPolicyTests
{
    [Fact]
    public async Task Default_Policy_Permits_All_Actions()
    {
        IAdminAuthorizationPolicy policy = new AllowAllAuthorizationPolicy();
        foreach (AdminAction action in Enum.GetValues<AdminAction>())
        {
            Assert.True(await policy.IsAuthorizedAsync("Todo", action, new ClaimsPrincipal()));
        }
    }

    [Fact]
    public async Task Custom_Policy_Can_Deny_By_Action()
    {
        IAdminAuthorizationPolicy policy = new DenyDeletes();
        Assert.True(
            await policy.IsAuthorizedAsync("Todo", AdminAction.Read, new ClaimsPrincipal())
        );
        Assert.True(
            await policy.IsAuthorizedAsync("Todo", AdminAction.Update, new ClaimsPrincipal())
        );
        Assert.False(
            await policy.IsAuthorizedAsync("Todo", AdminAction.Delete, new ClaimsPrincipal())
        );
    }

    [Fact]
    public async Task Custom_Policy_Sees_Entity_And_Instance()
    {
        IAdminAuthorizationPolicy policy = new InstanceAware();
        Assert.True(
            await policy.IsAuthorizedAsync("Todo", AdminAction.Update, new ClaimsPrincipal(), "ok")
        );
        Assert.False(
            await policy.IsAuthorizedAsync("Todo", AdminAction.Update, new ClaimsPrincipal(), "bad")
        );
    }

    private sealed class DenyDeletes : IAdminAuthorizationPolicy
    {
        public Task<bool> IsAuthorizedAsync(
            string entityName,
            AdminAction action,
            ClaimsPrincipal user,
            object? instance = null,
            string? actionName = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(action != AdminAction.Delete);
    }

    private sealed class InstanceAware : IAdminAuthorizationPolicy
    {
        public Task<bool> IsAuthorizedAsync(
            string entityName,
            AdminAction action,
            ClaimsPrincipal user,
            object? instance = null,
            string? actionName = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(instance is not string s || s == "ok");
    }
}
