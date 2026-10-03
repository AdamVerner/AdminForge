using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TodoApp.Data;

namespace AdminForge.IntegrationTests;

/// <summary>The panel as MCP tools, driven by the SDK's own client over a signed-in session.</summary>
public class McpToolsTests : IClassFixture<TodoAppFactory>, IClassFixture<DenyingTodoAppFactory>
{
    private readonly TodoAppFactory _factory;
    private readonly DenyingTodoAppFactory _readOnly;

    public McpToolsTests(TodoAppFactory factory, DenyingTodoAppFactory readOnly)
    {
        _factory = factory;
        _readOnly = readOnly;
    }

    [Fact]
    public async Task Tools_List_Shows_What_The_Policy_Allows()
    {
        await using var all = await ConnectAsync(_factory);
        var names = (await all.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                "search",
                "list_todo",
                "get_todo",
                "create_todo",
                "update_todo",
                "delete_todo",
                "todo_snooze",
                "form_send_notification",
                "dashboard_operations",
            },
            names
        );

        await using var readOnly = await ConnectAsync(_readOnly);
        var visible = (await readOnly.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.Contains("list_todo", visible);
        Assert.Contains("dashboard_operations", visible);
        Assert.DoesNotContain("create_todo", visible);
        Assert.DoesNotContain("todo_snooze", visible);
        Assert.DoesNotContain("form_send_notification", visible);
    }

    [Fact]
    public async Task Select_Choices_Arrive_In_The_Schema()
    {
        await using var client = await ConnectAsync(_factory);
        var snooze = (await client.ListToolsAsync()).Single(t => t.Name == "todo_snooze");
        var reason = snooze.JsonSchema.GetProperty("properties").GetProperty("Reason");
        Assert.Equal(
            ["waiting on someone", "low priority", "blocked"],
            reason.GetProperty("enum").EnumerateArray().Select(e => e.GetString())
        );
        Assert.Equal(
            ["key", "Days"],
            snooze.JsonSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
        );
    }

    [Fact]
    public async Task Read_Write_Act_Submit_And_Load_A_Dashboard()
    {
        using (var scope = _factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        await using var client = await ConnectAsync(_factory);

        // A todo belongs to a list: take the first one, then create and read back through list and get.
        var lists = Json(
            await client.CallToolAsync(
                "list_todo_list",
                new Dictionary<string, object?> { ["page_size"] = 1 }
            )
        );
        var listKey = lists.GetProperty("rows")[0].GetProperty("key").GetString()!;
        var created = Json(
            await client.CallToolAsync(
                "create_todo",
                new Dictionary<string, object?>
                {
                    ["Title"] = "From MCP",
                    ["Priority"] = "High",
                    ["TodoListId"] = listKey,
                }
            )
        );
        var key = created.GetProperty("key").GetString()!;

        var page = Json(
            await client.CallToolAsync(
                "list_todo",
                new Dictionary<string, object?> { ["page_size"] = 2, ["search"] = "From MCP" }
            )
        );
        Assert.True(page.GetProperty("total").GetInt32() >= 1);
        Assert.Contains(
            page.GetProperty("rows").EnumerateArray(),
            r => r.GetProperty("key").GetString() == key
        );

        // A list row carries the list columns and no navigation; a bad page or key is a plain error.
        var listed = page.GetProperty("rows")
            .EnumerateArray()
            .First(r => r.GetProperty("key").GetString() == key);
        Assert.True(listed.TryGetProperty("Title", out _));
        Assert.False(listed.TryGetProperty("Tags", out _));
        Assert.False(listed.TryGetProperty("TodoList", out _));
        var negative = await client.CallToolAsync(
            "list_todo",
            new Dictionary<string, object?> { ["page"] = -1 }
        );
        Assert.NotEqual(true, negative.IsError);
        var badKey = await client.CallToolAsync(
            "get_todo",
            new Dictionary<string, object?> { ["key"] = "abc" }
        );
        Assert.True(badKey.IsError);
        Assert.StartsWith("Invalid argument", Text(badKey));

        var row = Json(
            await client.CallToolAsync(
                "get_todo",
                new Dictionary<string, object?> { ["key"] = key }
            )
        );
        Assert.Equal("From MCP", row.GetProperty("Title").GetString());
        Assert.False(row.TryGetProperty("TodoList", out _));
        Assert.Equal("High", row.GetProperty("Priority").GetString());

        // Update one column; the rest stays.
        Assert.Equal(
            "Updated.",
            Text(
                await client.CallToolAsync(
                    "update_todo",
                    new Dictionary<string, object?>
                    {
                        ["key"] = key,
                        ["Title"] = "From MCP, renamed",
                    }
                )
            )
        );
        row = Json(
            await client.CallToolAsync(
                "get_todo",
                new Dictionary<string, object?> { ["key"] = key }
            )
        );
        Assert.Equal("From MCP, renamed", row.GetProperty("Title").GetString());
        Assert.Equal("High", row.GetProperty("Priority").GetString());

        // An action with fields; a missing required field is a validation error.
        var snoozed = await client.CallToolAsync(
            "todo_snooze",
            new Dictionary<string, object?>
            {
                ["key"] = key,
                ["Days"] = 2,
                ["Reason"] = "blocked",
            }
        );
        Assert.NotEqual(true, snoozed.IsError);
        Assert.Contains("Snoozed 2 day(s): blocked", Text(snoozed));
        var invalid = await client.CallToolAsync(
            "todo_snooze",
            new Dictionary<string, object?> { ["key"] = key }
        );
        Assert.True(invalid.IsError);
        Assert.Contains("Days", Text(invalid));

        // A form; its file field is not part of the schema.
        var sent = await client.CallToolAsync(
            "form_send_notification",
            new Dictionary<string, object?>
            {
                ["Title"] = "Hi",
                ["Body"] = "From MCP",
                ["Channel"] = "inbox",
            }
        );
        Assert.NotEqual(true, sent.IsError);

        // A dashboard.
        var dashboard = Json(
            await client.CallToolAsync("dashboard_operations", new Dictionary<string, object?>())
        );
        Assert.NotEmpty(dashboard.GetProperty("widgets").EnumerateObject());

        // Delete, then the row is gone.
        Assert.Equal(
            "Deleted.",
            Text(
                await client.CallToolAsync(
                    "delete_todo",
                    new Dictionary<string, object?> { ["key"] = key }
                )
            )
        );
        var missing = await client.CallToolAsync(
            "get_todo",
            new Dictionary<string, object?> { ["key"] = key }
        );
        Assert.True(missing.IsError);
        Assert.Contains("No row", Text(missing));
    }

    private static Task<McpClient> ConnectAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory
    ) => McpSession.ConnectAsync(factory);

    private static string Text(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static JsonElement Json(CallToolResult result)
    {
        Assert.False(result.IsError == true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement;
    }
}
