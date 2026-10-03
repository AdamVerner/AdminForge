using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AdminForge.IntegrationTests;

/// <summary>TodoApp signs its admins in itself, so a client first posts the login form.</summary>
public static partial class SignedInClient
{
    public static async Task<HttpClient> CreateSignedInClientAsync(
        this WebApplicationFactory<Program> factory,
        string username = "admin",
        string password = "admin",
        bool allowAutoRedirect = true
    )
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect }
        );
        await client.SignInAsync(username, password);
        return client;
    }

    public static async Task<HttpResponseMessage> SignInAsync(
        this HttpClient client,
        string username,
        string password
    )
    {
        var page = await client.GetStringAsync("/admin/login");
        var token = HiddenInput().Match(page);
        return await client.PostAsync(
            "/admin/login",
            new FormUrlEncodedContent([
                new(token.Groups[1].Value, token.Groups[2].Value),
                new("username", username),
                new("password", password),
            ])
        );
    }

    [GeneratedRegex("""<input type="hidden" name="([^"]+)" value="([^"]+)">""")]
    private static partial Regex HiddenInput();
}
