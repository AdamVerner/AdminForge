using System.Net;

namespace AdminForge.IntegrationTests;

public class LoginTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _factory;

    public LoginTests(TodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Sign_In_Then_Browse_Then_Sign_Out()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var anonymous = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("http://localhost/admin/login", anonymous.Headers.Location!.ToString());

        var wrong = await client.SignInAsync("admin", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("Wrong username or password", await wrong.Content.ReadAsStringAsync());

        var right = await client.SignInAsync("admin", "admin");
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.Equal("/admin", right.Headers.Location!.ToString());

        var panel = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Contains("Account menu", await panel.Content.ReadAsStringAsync());

        var signedOut = await client.PostAsync("/admin/logout", null);
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }
}
