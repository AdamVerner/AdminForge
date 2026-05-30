using AdminForge.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// The <c>NavigationPropertySelect</c> UI component exposes its data needs through
/// <see cref="IAdminUIBridge.SearchRelatedAsync"/> + <see cref="IAdminUIBridge.FindRelatedAsync"/>.
/// These bridge calls are the public contract the component relies on.
/// </summary>
public class NavigationPickerTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _factory;

    public NavigationPickerTests(TodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SearchRelatedAsync_Matches_DisplayName_Substring()
    {
        await SeedUsers();
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();

        var results = await bridge.SearchRelatedAsync(typeof(User), "lic", take: 10);

        var alice = Assert.Single(results, r => r.DisplayLabel.Contains("Alice", StringComparison.Ordinal));
        Assert.NotNull(alice.Key);
        Assert.Equal("User", alice.EntityName);
    }

    [Fact]
    public async Task FindRelatedAsync_Returns_Label_For_Current_Value()
    {
        var ids = await SeedUsers();
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();

        var nav = await bridge.FindRelatedAsync(typeof(User), ids.AliceId.ToString());
        Assert.NotNull(nav);
        Assert.Contains("Alice", nav!.DisplayLabel);
    }

    [Fact]
    public async Task FindRelatedAsync_Unknown_Key_Returns_Null()
    {
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var nav = await bridge.FindRelatedAsync(typeof(User), "99999");
        Assert.Null(nav);
    }

    private async Task<(int AliceId, int BobId)> SeedUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var alice = await db.Users.FirstOrDefaultAsync(u => u.Email == "alice@picker.test");
        if (alice is null)
        {
            alice = new User { DisplayName = "Alice", Email = "alice@picker.test" };
            db.Users.Add(alice);
            await db.SaveChangesAsync();
        }
        var bob = await db.Users.FirstOrDefaultAsync(u => u.Email == "bob@picker.test");
        if (bob is null)
        {
            bob = new User { DisplayName = "Bob", Email = "bob@picker.test" };
            db.Users.Add(bob);
            await db.SaveChangesAsync();
        }
        return (alice.Id, bob.Id);
    }
}
