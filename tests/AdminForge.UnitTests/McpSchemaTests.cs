using System.Text.Json;
using AdminForge.Core.Metadata;
using AdminForge.Mcp;

namespace AdminForge.UnitTests;

public class McpSchemaTests
{
    [Theory]
    [InlineData("Todo", "todo")]
    [InlineData("SiteSettings", "site_settings")]
    [InlineData("alert-rules", "alert_rules")]
    [InlineData("Enable experimental API (1 h)", "enable_experimental_api_1_h")]
    public void Slugs_Are_Snake_Case(string label, string slug) =>
        Assert.Equal(slug, ToolNames.Slug(label));

    [Theory]
    [InlineData(typeof(string), "string", null)]
    [InlineData(typeof(int?), "integer", null)]
    [InlineData(typeof(decimal), "number", null)]
    [InlineData(typeof(bool), "boolean", null)]
    [InlineData(typeof(DateTime), "string", "date-time")]
    [InlineData(typeof(DateOnly), "string", "date")]
    [InlineData(typeof(Guid), "string", null)]
    public void Column_Types_Map_To_Json(Type clr, string type, string? format)
    {
        var p = JsonSchema.ForClrType(clr, null);
        Assert.Equal(type, (string)p["type"]!);
        Assert.Equal(format, (string?)p["format"]);
    }

    [Fact]
    public void Enum_Columns_And_Select_Fields_List_Their_Choices()
    {
        var e = JsonSchema.ForClrType(typeof(DayOfWeek), null);
        Assert.Equal(7, e["enum"]!.AsArray().Count);

        var select = new FieldMeta
        {
            Name = "Reason",
            Label = "Reason",
            Kind = FieldKind.Select,
            Options = new SelectFieldOptions { Multiple = true },
        };
        var s = JsonSchema.ForField(select, [new("a", "Alpha"), new("b", "b")]);
        Assert.Equal("array", (string)s["type"]!);
        Assert.Equal(["a", "b"], s["items"]!["enum"]!.AsArray().Select(n => (string)n!));
        Assert.Contains("a = Alpha", (string)s["items"]!["description"]!);
    }

    [Fact]
    public void Field_Values_Come_Back_As_The_Handler_Expects()
    {
        object? Of(FieldKind kind, string json, object? options = null) =>
            JsonSchema.FieldValue(
                new FieldMeta
                {
                    Name = "f",
                    Label = "f",
                    Kind = kind,
                    Options = options,
                },
                JsonDocument.Parse(json).RootElement
            );

        Assert.Equal(3L, Of(FieldKind.Number, "3"));
        Assert.Equal(3L, Of(FieldKind.Number, "\"3\""));
        Assert.Equal(2.5, Of(FieldKind.Float, "2.5"));
        Assert.Equal(true, Of(FieldKind.Bool, "true"));
        Assert.Equal(
            new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc),
            Of(FieldKind.DateTime, "\"2026-10-03T00:00:00Z\"")
        );
        Assert.Equal("x", Of(FieldKind.Select, "\"x\""));
        Assert.Equal(
            new List<string> { "a", "b" },
            Of(FieldKind.Select, "[\"a\",\"b\"]", new SelectFieldOptions { Multiple = true })
        );
        Assert.Null(Of(FieldKind.Text, "null"));
    }
}
