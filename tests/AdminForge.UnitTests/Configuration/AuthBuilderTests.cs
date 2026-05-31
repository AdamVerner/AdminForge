using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

public class AuthBuilderTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void RequireAuthorizationPolicy_Stored_On_Options()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.RequireAuthorizationPolicy("AdminOnly").AddTable<User>();
        var options = builder.Build();
        Assert.Equal("AdminOnly", options.AuthorizationPolicy);
    }

    [Fact]
    public void Default_AuthorizationPolicy_Is_Null()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>();
        var options = builder.Build();
        Assert.Null(options.AuthorizationPolicy);
    }

    [Fact]
    public async Task WithAuditLog_Delegate_Wraps_To_Sink()
    {
        var fired = false;
        var builder = new AdminForgeBuilder(Scan());
        builder.WithAuditLog(
            (e, _) =>
            {
                fired = true;
                return Task.CompletedTask;
            }
        );

        var options = builder.Build();
        Assert.NotNull(options.AuditSink);
        await options.AuditSink!.RecordAsync(
            new AuditEvent { EntityType = "X", Action = AuditAction.Create }
        );
        Assert.True(fired);
    }

    [Fact]
    public void EntityMeta_Has_Default_RouteName_From_ClrType()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>();
        var meta = builder.Build().Entities.Single();
        Assert.Equal("User", meta.RouteName);
    }
}
