using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdminForge.Core.Metadata;

namespace AdminForge.Mcp;

/// <summary>Builds a tool's input schema from metadata and reads arguments back as CLR values.</summary>
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

    public static JsonObject Array(JsonObject items, string? description = null)
    {
        var p = Property("array", description);
        p["items"] = items;
        return p;
    }

    /// <summary>A column's scalar type as a schema property.</summary>
    public static JsonObject ForClrType(Type type, string? description)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum)
            return Enum(System.Enum.GetNames(t), description);
        if (t == typeof(bool))
            return Property("boolean", description);
        if (
            t == typeof(int)
            || t == typeof(long)
            || t == typeof(short)
            || t == typeof(byte)
            || t == typeof(uint)
            || t == typeof(ulong)
            || t == typeof(ushort)
        )
            return Property("integer", description);
        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal))
            return Property("number", description);
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
            return Property("string", description, "date-time");
        if (t == typeof(DateOnly))
            return Property("string", description, "date");
        return Property("string", description);
    }

    /// <summary>A form or action field as a schema property; select choices are filled in by <paramref name="choices"/>.</summary>
    public static JsonObject ForField(FieldMeta field, IReadOnlyList<SelectOption>? choices)
    {
        var d = field.Description;
        switch (field.Kind)
        {
            case FieldKind.Number:
                return Property("integer", d);
            case FieldKind.Float:
                return Property("number", d);
            case FieldKind.Bool:
                return Property("boolean", d);
            case FieldKind.Date:
                return Property("string", d, "date");
            case FieldKind.DateTime:
                return Property("string", d, "date-time");
            case FieldKind.Select:
            {
                var multiple = field.Options is SelectFieldOptions { Multiple: true };
                var item = choices is { Count: > 0 }
                    ? Enum(choices.Select(c => c.Value), Describe(choices, d))
                    : Property("string", d);
                return multiple ? Array(item, d) : item;
            }
            default:
                return Property("string", d);
        }
    }

    private static string Describe(IReadOnlyList<SelectOption> choices, string? description)
    {
        var legend = string.Join(
            ", ",
            choices.Where(c => c.Value != c.Label).Select(c => $"{c.Value} = {c.Label}")
        );
        return legend.Length == 0 ? description ?? "" : $"{description} ({legend})".Trim();
    }

    /// <summary>The CLR value a field handler expects for one argument.</summary>
    public static object? FieldValue(FieldMeta field, JsonElement e)
    {
        if (e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return field.Kind switch
        {
            FieldKind.Number => e.ValueKind == JsonValueKind.Number
                ? e.GetInt64()
                : long.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            FieldKind.Float => e.ValueKind == JsonValueKind.Number
                ? e.GetDouble()
                : double.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            FieldKind.Bool => e.ValueKind == JsonValueKind.True
                || (e.ValueKind == JsonValueKind.String && bool.Parse(e.GetString()!)),
            FieldKind.Date or FieldKind.DateTime => DateTime.Parse(
                e.GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
            FieldKind.Select when field.Options is SelectFieldOptions { Multiple: true } =>
                e.ValueKind == JsonValueKind.Array
                    ? e.EnumerateArray().Select(Scalar).Select(v => v?.ToString() ?? "").ToList()
                    : new List<string> { e.ToString() },
            _ => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString(),
        };
    }

    /// <summary>A JSON scalar as string, long, double or bool; the bridge coerces to the column type.</summary>
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
