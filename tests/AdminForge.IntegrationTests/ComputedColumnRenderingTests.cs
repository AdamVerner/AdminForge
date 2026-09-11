using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Computed values reach the entity view: a projection is re-asked of the database against the
/// real EF provider, and a resolver runs in-process against the loaded row. Asserted on the
/// bridge rather than the page, because an EF-backed detail page ships only the Blazor shell on
/// first paint.
/// </summary>
public class ComputedColumnRenderingTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _todo;

    public ComputedColumnRenderingTests(TodoAppFactory todo) => _todo = todo;

    private async Task<(IAdminUIBridge Bridge, EntityMeta Entity, IServiceScope Scope)> Open(
        string routeName
    )
    {
        var scope = _todo.Services.CreateScope();
        await TodoApp.Data.DbSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<TodoApp.Data.AppDbContext>()
        );
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var entity = bridge.FindEntityByRouteName(routeName);
        Assert.NotNull(entity);
        return (bridge, entity!, scope);
    }

    [Fact]
    public async Task A_Projection_Is_Re_Queried_For_One_Instance()
    {
        var (bridge, tag, scope) = await Open("Tag");
        using var _ = scope;

        // The "work" tag has three todos. FindAsync loads no collection navigations, so a
        // selector compiled against the loaded instance would count zero here.
        var view = await bridge.FindAsync(tag, "1");

        Assert.NotNull(view);
        Assert.Equal(3, Assert.Contains("TodoCount", view!.Values));
    }

    [Fact]
    public async Task A_Resolver_Runs_For_The_View_And_Replaces_The_Column_It_Hides()
    {
        var (bridge, todo, scope) = await Open("Todo");
        using var _ = scope;

        var view = await bridge.FindAsync(todo, "1");

        Assert.NotNull(view);
        var phrase = Assert.IsType<string>(Assert.Contains("DuePhrase", view!.Values));
        Assert.Matches(@"^(no date|today|overdue by \d+d|in \d+d)$", phrase);
        Assert.Contains("AdminEdits", view.Values);

        // The raw timestamp is still fetched — it is the view that hides it.
        Assert.True(todo.Columns.Single(c => c.PropertyName == "DueAt").HiddenInView);
    }

    [Fact]
    public async Task Only_The_Listed_Resolver_Runs_For_Every_Row()
    {
        var (bridge, todo, scope) = await Open("Todo");
        using var _ = scope;

        var list = await bridge.ListAsync(todo, new ListQuery { Page = 0, PageSize = 10 });

        Assert.NotEmpty(list.Rows);
        foreach (var row in list.Rows)
        {
            Assert.Contains("AdminEdits", row.Values);
            // DuePhrase is detail-only, so the table never pays for it.
            Assert.DoesNotContain("DuePhrase", row.Values);
        }
    }
}
