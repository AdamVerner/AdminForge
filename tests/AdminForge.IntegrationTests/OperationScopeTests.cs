using System.Security.Claims;
using AdminForge.Core.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TodoApp;

namespace AdminForge.IntegrationTests;

/// <summary>
/// A circuit lives for hours, so the bridge gives every operation a scope of its own and puts the
/// circuit's user in it: a provider sees a fresh scope per call and knows who is asking.
/// </summary>
public class OperationScopeTests : IClassFixture<ScopeProbeTodoAppFactory>
{
    private readonly ScopeProbeTodoAppFactory _factory;

    public OperationScopeTests(ScopeProbeTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Each_Operation_Gets_Its_Own_Scope_Carrying_The_Caller()
    {
        var probe = _factory.Services.GetRequiredService<ScopeProbe>();
        using var circuit = _factory.Services.CreateScope();
        var alice = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "Test")
        );
        circuit.ServiceProvider.GetRequiredService<OperationUserAccessor>().Set(alice);
        var bridge = circuit.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var settings = bridge.FindEntityByRouteName("SiteSettings")!;

        await bridge.ListAsync(settings, new ListQuery());
        var view = await bridge.FindAsync(settings, "1");

        Assert.NotNull(view);
        Assert.Equal(2, probe.Providers.Count);
        Assert.NotSame(probe.Providers[0], probe.Providers[1]);
        Assert.Equal(["alice", "alice"], probe.Callers);
    }
}

public class ScopeProbeTodoAppFactory : WebApplicationFactory<Program>
{
    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-scope-{Guid.NewGuid():N}.db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ScopeProbe>();
            services.AddScoped<IAdminDataProvider<SiteSettings>, ProbingSiteSettingsProvider>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch { }
    }
}

public sealed class ScopeProbe
{
    public List<object> Providers { get; } = [];
    public List<string?> Callers { get; } = [];
}

/// <summary>Records which instance served each call and whom the scope said was calling.</summary>
public sealed class ProbingSiteSettingsProvider : IAdminDataProvider<SiteSettings>
{
    private readonly SiteSettingsDataProvider _inner;

    public ProbingSiteSettingsProvider(SiteSettingsStore store, IUserAccessor user, ScopeProbe probe)
    {
        _inner = new SiteSettingsDataProvider(store);
        probe.Providers.Add(this);
        probe.Callers.Add(user.GetUserId());
    }

    public Task<ListResult<SiteSettings>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => _inner.ListAsync(query, cancellationToken);

    public Task<SiteSettings?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => _inner.FindAsync(keyValues, cancellationToken);

    public Task<SiteSettings> CreateAsync(
        SiteSettings entity,
        CancellationToken cancellationToken = default
    ) => _inner.CreateAsync(entity, cancellationToken);

    public Task<SiteSettings> UpdateAsync(
        SiteSettings entity,
        CancellationToken cancellationToken = default
    ) => _inner.UpdateAsync(entity, cancellationToken);

    public Task<bool> DeleteAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => _inner.DeleteAsync(keyValues, cancellationToken);
}
