namespace AdminForge.Mcp;

/// <summary>The MCP surface's scheme, policy and the claims a connection's principal carries.</summary>
public static class AdminForgeMcp
{
    public const string Scheme = "AdminForge.Mcp";
    public const string Policy = "AdminForge.McpConnection";

    /// <summary>The connection id; present on every principal that came in with a bearer token.</summary>
    public const string ConnectionClaim = "adminforge:connection";

    /// <summary>One per tool the connection was granted.</summary>
    public const string ToolClaim = "adminforge:tool";
}
