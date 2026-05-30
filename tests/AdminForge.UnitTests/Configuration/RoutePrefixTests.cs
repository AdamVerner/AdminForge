#pragma warning disable CS0618 // Intentionally exercising the obsolete API.

using AdminForge.Core.Configuration;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;

namespace AdminForge.UnitTests.Configuration;

/// <summary>
/// Phase 3 limitation: Blazor @page routes are compile-time literals pinned to /admin.
/// Builder accepts the default but rejects any other prefix until the router-rewriter lands.
/// </summary>
public class RoutePrefixTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void Default_Prefix_Is_Admin()
    {
        var options = new AdminForgeBuilder(Scan()).Build();
        Assert.Equal("admin", options.RoutePrefix);
    }

    [Fact]
    public void WithRoutePrefix_Admin_Is_Accepted()
    {
        var options = new AdminForgeBuilder(Scan()).WithRoutePrefix("admin").Build();
        Assert.Equal("admin", options.RoutePrefix);
    }

    [Fact]
    public void WithRoutePrefix_Strips_Leading_Slash()
    {
        var options = new AdminForgeBuilder(Scan()).WithRoutePrefix("/admin").Build();
        Assert.Equal("admin", options.RoutePrefix);
    }

    [Fact]
    public void WithRoutePrefix_NonDefault_Throws()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<NotSupportedException>(() => builder.WithRoutePrefix("backoffice"));
    }
}
