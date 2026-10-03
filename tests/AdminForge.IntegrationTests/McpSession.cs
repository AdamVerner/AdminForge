using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace AdminForge.IntegrationTests;

/// <summary>Connects the SDK's client through the panel's OAuth, with a signed-in HttpClient playing the browser.</summary>
public static partial class McpSession
{
    public static async Task<McpClient> ConnectAsync(
        WebApplicationFactory<Program> factory,
        Func<IReadOnlyList<string>, IEnumerable<string>>? pick = null,
        string clientName = "Test agent"
    )
    {
        var browser = await factory.CreateSignedInClientAsync(allowAutoRedirect: false);
        var http = factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                OAuth = new ClientOAuthOptions
                {
                    RedirectUri = new Uri("http://localhost/callback"),
                    DynamicClientRegistration = new() { ClientName = clientName },
                    AuthorizationCallbackHandler = async (context, ct) =>
                    {
                        var query = await ConsentAsync(
                            browser,
                            context.AuthorizationUri,
                            pick ?? (all => all),
                            ct
                        );
                        return new AuthorizationResult
                        {
                            Code = query["code"].ToString(),
                            State = query["state"].ToString(),
                            Iss = query["iss"].ToString(),
                        };
                    },
                },
            },
            http
        );
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>Opens the consent page as the signed-in admin, ticks tools, submits, and returns the callback query.</summary>
    public static async Task<
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
    > ConsentAsync(
        HttpClient browser,
        Uri authorizeUri,
        Func<IReadOnlyList<string>, IEnumerable<string>> pick,
        CancellationToken ct
    )
    {
        var page = await browser.GetAsync(authorizeUri, ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(ct);

        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match m in HiddenInput().Matches(html))
            fields.Add(
                new(
                    WebUtility.HtmlDecode(m.Groups[1].Value),
                    WebUtility.HtmlDecode(m.Groups[2].Value)
                )
            );
        var offered = ToolCheckbox().Matches(html).Select(m => m.Groups[1].Value).ToList();
        foreach (var tool in pick(offered))
            fields.Add(new("tools", tool));
        fields.Add(new("expires", "60"));
        fields.Add(new("decision", "allow"));

        var action = FormAction().Match(html).Groups[1].Value;
        var redirect = await browser.PostAsync(
            new Uri(authorizeUri, action),
            new FormUrlEncodedContent(fields),
            ct
        );
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        return QueryHelpers.ParseQuery(redirect.Headers.Location!.Query);
    }

    [GeneratedRegex("""<input type="hidden" name="([^"]+)" value="([^"]*)">""")]
    private static partial Regex HiddenInput();

    [GeneratedRegex("<input type=\"checkbox\" name=\"tools\" value=\"([^\"]+)\"")]
    private static partial Regex ToolCheckbox();

    [GeneratedRegex("""<form method="post" action="([^"]+)">""")]
    private static partial Regex FormAction();
}
