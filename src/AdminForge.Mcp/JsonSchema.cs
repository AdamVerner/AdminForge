using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdminForge.Mcp;

/// <summary>Builds a tool's input schema from the model and reads arguments back as plain values.</summary>
internal static class JsonSchema
{
    public static JsonElement Object(JsonObject properties, IEnumerable<string> required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        var req = new JsonArray([.. required.Select(r => (JsonNode)r)]);
        if (req.Count > 0)
            schema["required"] = req;
        return JsonSerializer.SerializeToElement(schema);
    }

    public static JsonObject Property(
        string type,
        string? description = null,
        string? format = null
    )
    {
        var p = new JsonObject { ["type"] = type };
        if (description is not null)
            p["description"] = description;
        if (format is not null)
            p["format"] = format;
        return p;
    }

    public static JsonObject Enum(IEnumerable<string> values, string? description = null)
    {
        var p = Property("string", description);
        p["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]);
        return p;
    }

    /// <summary>A value of <paramref name="type"/> as a schema property.</summary>
    public static JsonObject ForClrType(Type type, string? description)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum)
            return Enum(System.Enum.GetNames(t), description);
        if (t == typeof(bool))
            return Property("boolean", description);
        if (ScalarTypes.IsInteger(t))
            return Property("integer", description);
        if (ScalarTypes.IsFraction(t))
            return Property("number", description);
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
            return Property("string", description, "date-time");
        if (t == typeof(DateOnly))
            return Property("string", description, "date");
        return Property("string", description);
    }

    /// <summary>Every field a tool call can carry; a file cannot travel in one.</summary>
    public static JsonObject Fields(
        IEnumerable<FieldModel> fields,
        IReadOnlyDictionary<string, IReadOnlyList<AdminOption>>? choices = null
    )
    {
        var p = new JsonObject();
        foreach (var f in fields.Where(f => f.Kind != FieldKind.File))
            p[f.Name] = ForField(f, choices?.GetValueOrDefault(f.Name));
        return p;
    }

    /// <summary>An input as a schema property; a select's choices come from <paramref name="choices"/>.</summary>
    public static JsonObject ForField(FieldModel field, IReadOnlyList<AdminOption>? choices)
    {
        var d = field.Description ?? field.Title;
        if (field.Kind != FieldKind.Select)
            return ForClrType(field.ClrType, d);
        var legend = string.Join(
            ", ",
            choices?.Where(c => c.Value != c.Label).Select(c => $"{c.Value} = {c.Label}") ?? []
        );
        var described = legend.Length == 0 ? d : $"{d} ({legend})";
        var item = choices is { Count: > 0 }
            ? Enum(choices.Select(c => c.Value), described)
            : Property("string", described);
        if (!field.Multiple)
            return item;
        var array = Property("array", d);
        array["items"] = item;
        return array;
    }

    /// <summary>A JSON scalar as string, long, double or bool; the panel converts it to the field's type.</summary>
    public static object? Scalar(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => e.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            _ => e.GetRawText(),
        };
}
