using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdminForge.Mcp;

/// <summary>One panel operation as a tool. Every call runs through the bridge from the request's scope.</summary>
internal abstract class PanelTool(AdminForgeMcpOptions options) : McpServerTool
{
    protected AdminForgeMcpOptions Options { get; } = options;
    protected JsonSerializerOptions Json => Options.JsonSerializerOptions;

    private Tool? _tool;
    public override Tool ProtocolTool => _tool ??= Describe(null);
    public override IReadOnlyList<object> Metadata => [];

    /// <summary>The tool's description; <paramref name="bridge"/> is present when choices can be loaded.</summary>
    protected abstract Tool Describe(IAdminUIBridge? bridge);

    /// <summary>Where the consent page files the tool: the owner's sidebar entry and its name.</summary>
    public abstract NavMeta Nav { get; }
    public abstract string Section { get; }

    /// <summary>A bearer connection may call only what it was granted; a panel session may call anything the policy allows.</summary>
    public static bool Granted(ClaimsPrincipal user, string toolName) =>
        !user.HasClaim(c => c.Type == AdminForgeMcp.ConnectionClaim)
        || user.HasClaim(AdminForgeMcp.ToolClaim, toolName);

    /// <summary>Whether the caller may run this tool, as the panel would ask before showing its button.</summary>
    public abstract Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct);

    /// <summary>Re-describes a tool whose schema depends on runtime data, such as select choices.</summary>
    public virtual Task RefreshAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        Task.CompletedTask;

    protected void Replace(Tool tool) => _tool = tool;

    protected abstract Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    );

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default
    )
    {
        var services =
            request.Services
            ?? throw new InvalidOperationException("MCP requests must run in a DI scope.");
        var bridge = services.GetRequiredService<IAdminUIBridge>();
        if (!Granted(services.GetRequiredService<IUserAccessor>().GetUser(), ProtocolTool.Name))
            return Error($"Forbidden: this connection was not granted {ProtocolTool.Name}.");
        var args = new ToolArgs(request.Params?.Arguments);
        try
        {
            return await RunAsync(bridge, args, cancellationToken).ConfigureAwait(false);
        }
        catch (AdminForbiddenException)
        {
            return Error($"Forbidden: {ProtocolTool.Name}.");
        }
        catch (FormValidationException ex)
        {
            return Error(
                "Invalid input:\n"
                    + string.Join('\n', ex.Errors.Select(e => $"- {e.Key}: {e.Value}"))
            );
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return Error($"Invalid argument: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A handler's failure is the tool's error text, as the panel's snackbar would show it.
            return Error(ex.GetBaseException().Message);
        }
    }

    protected static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    protected CallToolResult JsonResult(object? value) =>
        Text(JsonSerializer.Serialize(value, Json));

    protected static CallToolResult Error(string text) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

    protected static CallToolResult NotFound(string key) => Error($"No row with key '{key}'.");

    /// <summary>
    /// A row as the agent sees it: its key and its values, written with the host's options. A
    /// navigation is never a value, because a list does not load it; a list row carries only the
    /// list columns, as its tool promises.
    /// </summary>
    protected JsonObject Row(
        EntityMeta entity,
        string key,
        IReadOnlyDictionary<string, object?> values,
        bool listed
    )
    {
        var columns = entity.Columns.ToDictionary(c => c.PropertyName, StringComparer.Ordinal);
        var o = new JsonObject { ["key"] = key };
        foreach (var (name, value) in values)
        {
            if (columns.TryGetValue(name, out var column))
            {
                if (
                    column.Kind is ColumnKind.NavigationReference or ColumnKind.NavigationCollection
                )
                    continue;
                if (listed && !column.ShowInList)
                    continue;
            }
            o[name] = JsonSerializer.SerializeToNode(
                value,
                value?.GetType() ?? typeof(object),
                Json
            );
        }
        return o;
    }

    protected static Tool Tool(
        string name,
        string description,
        JsonElement schema,
        bool readOnly = false,
        bool destructive = false,
        bool idempotent = false
    ) =>
        new()
        {
            Name = name,
            Description = description,
            InputSchema = schema,
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = readOnly,
                DestructiveHint = destructive,
                IdempotentHint = idempotent,
                OpenWorldHint = false,
            },
        };
}

/// <summary>The call's arguments by name.</summary>
internal readonly struct ToolArgs(IDictionary<string, JsonElement>? values)
{
    private readonly IDictionary<string, JsonElement> _values =
        values ?? new Dictionary<string, JsonElement>();

    public bool TryGet(string name, out JsonElement element) =>
        _values.TryGetValue(name, out element)
        && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    public string Required(string name) =>
        TryGet(name, out var e)
            ? (e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString())
            : throw new ArgumentException($"'{name}' is required.");

    public string? String(string name) =>
        TryGet(name, out var e)
            ? (e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
            : null;

    public int? Int(string name) => TryGet(name, out var e) ? e.GetInt32() : null;

    public bool Bool(string name) => TryGet(name, out var e) && e.ValueKind == JsonValueKind.True;

    public IEnumerable<KeyValuePair<string, JsonElement>> All => _values;
}
