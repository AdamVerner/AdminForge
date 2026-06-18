using System.Net;
using AdminForge.Core.Contracts;
using AdminForge.Core.LiveUpdates;
using AdminForge.Core.Metadata;
using AdminForge.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.IntegrationTests;

/// <summary>
/// Bridge-level coverage for the live-update surface. The only multicasted source is
/// the streaming line chart on the operations dashboard; entity views poll directly
/// via the existing find-by-key path.
/// </summary>
public class LiveUpdatesTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _factory;

    public LiveUpdatesTests(TodoAppFactory factory) => _factory = factory;

    [Fact]
    public void Entity_View_Polling_Interval_Is_Surfaced_On_Meta()
    {
        // The Todo entity registers WithLivePolling(5s) — the bridge must expose it
        // on EntityMeta so the EntityViewPage can wire a Task.Delay loop.
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var entity = bridge.FindEntityByRouteName("Todo");
        Assert.NotNull(entity);
        Assert.Equal(TimeSpan.FromSeconds(5), entity!.LivePollingInterval);
    }

    [Fact]
    public async Task Entity_View_Page_Renders_When_Polling_Configured()
    {
        // Seed a row so /admin/entities/Todo/{id} has something to materialise.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<TodoApp.Data.AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var user = new TodoApp.Entities.User { DisplayName = "Liv", Email = "liv@example.com" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var list = new TodoApp.Entities.TodoList { Name = "Live", OwnerId = user.Id };
            db.TodoLists.Add(list);
            await db.SaveChangesAsync();
            db.Todos.Add(new TodoApp.Entities.Todo { Title = "Polled row", TodoListId = list.Id });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/admin/entities/Todo/1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Streaming_Line_Chart_Subscription_Yields_Updates_To_Two_Subscribers()
    {
        // The operations dashboard registers a streaming chart fed by MetricsTickStream.
        // Both subscribers must observe the same underlying multicast source.
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var dashboard = bridge.FindDashboardByRouteName("operations");
        Assert.NotNull(dashboard);

        var streamingWidget = dashboard!
            .Widgets.OfType<LineChartMeta>()
            .FirstOrDefault(w => w.LiveDataSource is not null);
        Assert.NotNull(streamingWidget);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var a = bridge.SubscribeLineChart(dashboard, streamingWidget!.Id, cts.Token);
        var b = bridge.SubscribeLineChart(dashboard, streamingWidget.Id, cts.Token);
        Assert.NotNull(a);
        Assert.NotNull(b);

        LiveUpdate<LineChartPoint>? fromA = null;
        LiveUpdate<LineChartPoint>? fromB = null;
        var aTask = Task.Run(async () =>
        {
            await foreach (var u in a!.WithCancellation(cts.Token))
            {
                fromA = u;
                break;
            }
        });
        var bTask = Task.Run(async () =>
        {
            await foreach (var u in b!.WithCancellation(cts.Token))
            {
                fromB = u;
                break;
            }
        });
        await Task.WhenAll(aTask, bTask);
        Assert.NotNull(fromA);
        Assert.NotNull(fromB);
    }
}
