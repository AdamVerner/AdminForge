using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Mcp;

public sealed class AdminForgeMcpOptions
{
    /// <summary>Where the server is mounted; the OAuth endpoints sit under it at <c>/oauth</c>.</summary>
    public string Path { get; set; } = "/mcp";

    /// <summary>
    /// The origin every URL in the OAuth documents is built from. Null reads the request, which a
    /// host behind a proxy must not trust for a security document.
    /// </summary>
    public Uri? PublicBaseUrl { get; set; }

    /// <summary>How row values and dashboard data are written; the host's converters apply.</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Rows per page when a list call names none, and the most it may ask for.</summary>
    public int DefaultPageSize { get; set; } = 25;
    public int MaxPageSize { get; set; } = 200;

    /// <summary>Rows of each related link that travel with a get.</summary>
    public int RelatedRows { get; set; } = 25;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>What the consent page offers for a connection's lifetime; the first is the default.</summary>
    public IReadOnlyList<TimeSpan> ConnectionLifetimes { get; set; } =
    [
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(8),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    internal Uri BaseUrl(HttpContext http) =>
        PublicBaseUrl
        ?? new Uri($"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/");
}
