using System.Collections;
using System.Globalization;

namespace AdminForge;

/// <summary>Converts submitted values to their fields' types and checks every rule, on the server, for every surface.</summary>
internal static class Validator
{
    /// <summary>The values as their fields' types; throws <see cref="AdminRejection"/> with one message per bad field.</summary>
    /// <param name="partial">An update: a field not submitted keeps its stored value.</param>
    public static async Task<Dictionary<string, object?>> CheckAsync(
        IReadOnlyList<FieldModel> fields,
        IReadOnlyDictionary<string, object?> submitted,
        bool partial,
        Operation op
    )
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in submitted.Keys.Where(n => fields.All(f => f.Name != n)))
            errors[name] = $"'{name}' cannot be set here.";

        foreach (var field in fields)
        {
            var given = submitted.TryGetValue(field.Name, out var raw);
            if (partial && !given)
                continue;
            if (!ScalarTypes.TryCoerce(raw, field.ClrType, out var value))
            {
                errors[field.Name] = $"{field.Title}: '{raw}' is not a valid value.";
                continue;
            }
            if (value is null or "" or ICollection { Count: 0 })
            {
                if (field.Required)
                    errors[field.Name] = $"{field.Title} is required.";
                else if (given)
                    values[field.Name] = value;
                continue;
            }
            var error = await CheckAsync(field, value, op).ConfigureAwait(false);
            if (error is not null)
                errors[field.Name] = error;
            else
                values[field.Name] = value;
        }
        if (errors.Count > 0)
            throw new AdminRejection(errors);
        return values;
    }

    private static async Task<string?> CheckAsync(FieldModel field, object value, Operation op)
    {
        if (value is Enum e && !Enum.IsDefined(e.GetType(), e))
            return $"{field.Title}: '{value}' is not one of {string.Join(", ", Enum.GetNames(e.GetType()))}.";
        if (field.Options is { } load)
        {
            // Checked against the choices as they are now, not as they were when the field showed.
            var allowed = (await load(op.Services, op.CancellationToken).ConfigureAwait(false))
                .Select(o => o.Value)
                .ToHashSet(StringComparer.Ordinal);
            var chosen = value is string or not IEnumerable
                ? [Convert.ToString(value, CultureInfo.InvariantCulture)!]
                : ((IEnumerable)value)
                    .Cast<object?>()
                    .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)!);
            if (chosen.FirstOrDefault(c => !allowed.Contains(c)) is { } bad)
                return $"{field.Title}: '{bad}' is not a choice.";
        }
        if (value is AdminFile file)
        {
            if (field.MaxBytes is { } max && file.Length > max)
                return $"{field.Title} is larger than {max.ToString(CultureInfo.InvariantCulture)} bytes.";
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (field.Accept is { Count: > 0 } accept && !accept.Contains(extension))
                return $"{field.Title}: '{extension}' files are not accepted; {string.Join(", ", accept)} are.";
        }
        return field.Rules.Select(rule => rule(value)).FirstOrDefault(m => m is not null);
    }
}
