using System.Net;

namespace AdminForge.IntegrationTests;

/// <summary>A type off the DbContext is served by the provider the host registers.</summary>
public class NonEfEntityTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _todo;

    public NonEfEntityTests(TodoAppFactory todo) => _todo = todo;

    [Fact]
    public async Task A_Provider_Backed_Entity_Lists_Shows_And_Offers_Editing()
    {
        var client = _todo.CreateClient();

        var list = await client.GetAsync("/admin/entities/SiteSettings");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Contains("Welcome to Todo Admin!", listBody);
        Assert.Contains("New Site Settings", listBody);

        var view = await client.GetAsync("/admin/entities/SiteSettings/1");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Contains("Welcome to Todo Admin!", await view.Content.ReadAsStringAsync());
    }
}
