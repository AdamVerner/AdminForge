using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AdminForge.Mcp;

/// <summary>
/// One panel operation as a tool. Every call goes through <see cref="AdminPanel"/>, so the umbrella
/// policy, the host's policy and the audit apply to each call, not to the connection.
/// </summary>
internal abstract class PanelTool : McpServerTool
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private Tool? _tool;
    public override Tool ProtocolTool => _tool ??= Describe();
    public override IReadOnlyList<object> Metadata => [];

    protected abstract Tool Describe();

    /// <summary>Where the consent page files the tool: its owner's sidebar entry and title.</summary>
    public abstract NavModel Nav { get; }
    public abstract string Section { get; }

    /// <summary>Whether the caller may run this tool at all, as tools/list and the consent page ask.</summary>
    public abstract Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct);

    /// <summary>The tool as this caller sees it; one with select fields lists the choices loaded for them.</summary>
    public virtual Task<Tool> DescribeAsync(AdminPanel panel, CancellationToken ct) =>
        Task.FromResult(ProtocolTool);

    public static bool Granted(ClaimsPrincipal user, string toolName) =>
        user.HasClaim(AdminForgeMcp.ToolClaim, toolName);

    protected abstract Task<CallToolResult> RunAsync(
        AdminPanel panel,
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
        var user = services.GetRequiredService<IUserAccessor>().GetUser();
        if (!Granted(user, ProtocolTool.Name))
            return Error($"Forbidden: this connection was not granted {ProtocolTool.Name}.");
        try
        {
            return await RunAsync(
                    Panel(services),
                    new ToolArgs(request.Params?.Arguments, user),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (AdminForbiddenException)
        {
            return Error($"Forbidden: {ProtocolTool.Name}.");
        }
        catch (AdminRejection ex)
        {
            return Error(
                ex.FieldErrors.Count == 0
                    ? ex.Message
                    : "Invalid input:\n"
                        + string.Join('\n', ex.FieldErrors.Select(e => $"- {e.Key}: {e.Value}"))
            );
        }
        catch (ArgumentException ex)
        {
            return Error($"Invalid argument: {ex.Message}");
        }
    }

    /// <summary>"SiteSettings" → "site_settings".</summary>
    public static string Snake(string id) => Members.Kebab(id).Replace('-', '_');

    public static AdminPanel Panel(IServiceProvider services)
    {
        var panel = services.GetRequiredService<AdminPanel>();
        panel.Surface = AdminSurface.Mcp;
        return panel;
    }

    protected static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    /// <summary>What the handler showed, or <paramref name="otherwise"/> when it showed nothing.</summary>
    protected static CallToolResult Text(Outcome outcome, string otherwise) =>
        Text(
            string.Join(
                "\n\n",
                new[]
                {
                    outcome.Message,
                    outcome.Markdown,
                    outcome.Open is { } key ? $"Opened the row with key {key}." : null,
                }.Where(s => s is not null)
            )
                is { Length: > 0 } text
                ? text
                : otherwise
        );

    protected static CallToolResult JsonResult(JsonNode value) => Text(value.ToJsonString(Json));

    protected static CallToolResult Error(string text) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

    protected static JsonNode? Value(object? value) =>
        JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object), Json);

    /// <summary>A row as the agent sees it: its key and its values.</summary>
    protected static JsonObject Row(RowView row)
    {
        var o = new JsonObject { ["key"] = row.Key };
        foreach (var (name, value) in row.Values)
            o[name] = Value(value);
        return o;
    }

    protected static JsonArray Rows(IEnumerable<RowView> rows) =>
        new([.. rows.Select(r => (JsonNode)Row(r))]);

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
internal readonly struct ToolArgs(IDictionary<string, JsonElement>? values, ClaimsPrincipal user)
{
    /// <summary>Whether the connection was granted a tool that reads <paramref name="tableId"/>'s rows.</summary>
    public bool GrantsRowsOf(string tableId) =>
        PanelTool.Granted(user, $"list_{PanelTool.Snake(tableId)}")
        || PanelTool.Granted(user, $"get_{PanelTool.Snake(tableId)}");

    private readonly IDictionary<string, JsonElement> _values =
        values ?? new Dictionary<string, JsonElement>();

    public bool TryGet(string name, out JsonElement element) =>
        _values.TryGetValue(name, out element)
        && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    public string Required(string name) =>
        String(name) ?? throw new ArgumentException($"'{name}' is required.");

    public string? String(string name) =>
        TryGet(name, out var e)
            ? (e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
            : null;

    // Agents often send numbers and booleans as strings; either form is taken, anything else refused.
    public int? Int(string name) =>
        !TryGet(name, out var e) ? null
        : e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i
        : e.ValueKind == JsonValueKind.String
            && int.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
        : throw new ArgumentException($"'{name}' must be an integer.");

    public bool Bool(string name) =>
        !TryGet(name, out var e) ? false
        : e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean()
        : e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out var parsed) ? parsed
        : throw new ArgumentException($"'{name}' must be true or false.");

    /// <summary>Every argument but the key, as values the panel converts and validates; one it does not know is refused there.</summary>
    public Dictionary<string, object?> Values()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, e) in _values.Where(a => a.Key != "key"))
            values[name] =
                e.ValueKind == JsonValueKind.Array
                    ? e.EnumerateArray()
                        .Select(i => Convert.ToString(JsonSchema.Scalar(i), CultureInfo.InvariantCulture) ?? "")
                        .ToList()
                    : JsonSchema.Scalar(e);
        return values;
    }
}
