using Microsoft.AspNetCore.Http;

namespace AdminForge;

public sealed class AdminForgeMcpOptions
{
    /// <summary>Where the server is mounted; the OAuth endpoints sit under it at <c>/oauth</c>.</summary>
    public string Path { get; set; } = "/mcp";

    /// <summary>
    /// The origin every URL in the OAuth documents is built from. Null reads the request, which a
    /// host behind a proxy must not trust for a security document.
    /// </summary>
    public Uri? PublicBaseUrl { get; set; }

    internal Uri BaseUrl(HttpContext http) =>
        PublicBaseUrl
        ?? new Uri($"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/");
}
