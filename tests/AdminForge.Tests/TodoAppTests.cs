using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AdminForge.Tests;

/// <summary>The example boots, signs an admin in and serves every page its sidebar links to.</summary>
public class TodoAppTests : IClassFixture<TodoAppTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _db = Path.Combine(
            Path.GetTempPath(),
            $"todoapp-{Guid.NewGuid():N}.db"
        );

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_db}");

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(_db);
        }
    }

    private readonly Factory _factory;

    public TodoAppTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Every_sidebar_link_opens()
    {
        var browser = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        var signedIn = await PanelHost.SignInAsync(browser, "admin", "admin");
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        var home = new Page(await browser.GetStringAsync("/admin"));
        var links = home.Links("[data-af^='nav:'] a").Distinct().ToList();
        Assert.Contains("/admin/entities/todo", links);
        Assert.Contains("/admin/status", links);
        // The account menu renders once the circuit starts, so its entry is named here.
        links.Add("/admin/entities/my-activity");
        foreach (var link in links)
        {
            var page = await OpenAsync(browser, link);
            // A table's first row opens too: detail pages carry the related tables and live widgets.
            var row = page.Links("a")
                .FirstOrDefault(h =>
                    h.StartsWith($"{link}/")
                    && h[(link.Length + 1)..] is var k
                    && k != "new"
                    && !k.Contains('/')
                    && !k.Contains('?')
                );
            if (link.StartsWith("/admin/entities/") && row is not null)
                await OpenAsync(browser, row);
        }
    }

    private static async Task<Page> OpenAsync(HttpClient browser, string link)
    {
        var response = await browser.GetAsync(link);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{link}: {response.StatusCode}");
        var page = new Page(await response.Content.ReadAsStringAsync());
        Assert.True(page.Has("title"), link);
        Assert.False(page.Has("refused"), link);
        Assert.False(page.Has("error"), link);
        Assert.DoesNotContain("failed", page.Html, StringComparison.OrdinalIgnoreCase);
        return page;
    }
}
