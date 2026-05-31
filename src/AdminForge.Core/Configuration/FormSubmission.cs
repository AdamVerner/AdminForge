using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Strongly-typed value bag handed to a form's <c>Submit</c> handler. Carries
/// the raw field values plus any uploaded files. Constructed by the renderer
/// at submission time; immutable from the handler's perspective.
/// </summary>
public sealed class FormSubmission
{
    private readonly IReadOnlyDictionary<string, object?> _values;
    private readonly IReadOnlyDictionary<string, FormFileUpload> _files;

    /// <summary>
    /// Build a submission. <paramref name="files"/> is optional — pass null for
    /// forms with no file-upload fields.
    /// </summary>
    public FormSubmission(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, FormFileUpload>? files = null
    )
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
        _files = files ?? new Dictionary<string, FormFileUpload>(StringComparer.Ordinal);
    }

    /// <summary>Field names → submitted values, in their raw form (string, bool, DateTime, etc.).</summary>
    public IReadOnlyDictionary<string, object?> Values => _values;

    /// <summary>Field names → uploaded files (only populated for <c>FileUpload</c> fields).</summary>
    public IReadOnlyDictionary<string, FormFileUpload> Files => _files;

    /// <summary>Indexer over <see cref="Values"/>; returns null when missing.</summary>
    public object? this[string name] => _values.TryGetValue(name, out var v) ? v : null;

    /// <summary>
    /// Best-effort typed accessor. Returns <c>default</c> when the field is
    /// missing or null; coerces strings to common scalar types so handlers can
    /// stay in idiomatic C#.
    /// </summary>
    public T? Get<T>(string name)
    {
        TryGet<T>(name, out var value);
        return value;
    }

    /// <summary>
    /// Typed lookup with explicit success signal. Returns false when the key is
    /// missing, the stored value is null, or coercion to <typeparamref name="T"/>
    /// fails.
    /// </summary>
    public bool TryGet<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        value = default;
        if (!_values.TryGetValue(name, out var raw) || raw is null)
            return false;

        if (raw is T direct)
        {
            value = direct;
            return true;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            if (targetType.IsEnum && raw is string enumStr)
            {
                value = (T)Enum.Parse(targetType, enumStr, ignoreCase: true);
                return true;
            }
            var converted = Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
            value = (T)converted;
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}

/// <summary>
/// One uploaded file, captured into memory by the renderer before the handler runs.
/// Call <see cref="OpenReadStream"/> for an isolated stream over the bytes — every
/// call returns a fresh stream over the same buffer.
/// </summary>
public sealed class FormFileUpload
{
    private readonly byte[] _bytes;

    public FormFileUpload(string fileName, string contentType, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(bytes);
        FileName = fileName;
        ContentType = contentType;
        _bytes = bytes;
    }

    public string FileName { get; }
    public string ContentType { get; }
    public long Length => _bytes.LongLength;

    /// <summary>Returns a fresh, seekable stream over the captured bytes.</summary>
    public Stream OpenReadStream() => new MemoryStream(_bytes, writable: false);
}
