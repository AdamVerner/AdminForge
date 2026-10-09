namespace AdminForge;

/// <summary>An expected refusal from a source, an action or a form: shown once, never audited.</summary>
/// <remarks>MCP returns it as the tool's error. Any other exception is logged and shown as a generic failure.</remarks>
public class AdminRejection : Exception
{
    public AdminRejection(string message)
        : base(message) => FieldErrors = new Dictionary<string, string>();

    /// <summary>Refuses particular fields; each message shows beside its field.</summary>
    public AdminRejection(IReadOnlyDictionary<string, string> fieldErrors)
        : base(string.Join(" ", fieldErrors.Values)) => FieldErrors = fieldErrors;

    /// <summary>Field name → message.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; }
}

/// <summary>An uploaded file, held in memory. A property of this type on an input record is an upload field.</summary>
public sealed class AdminFile(string name, string contentType, byte[] content)
{
    public string Name { get; } = name;
    public string ContentType { get; } = contentType;
    public long Length => content.LongLength;

    /// <summary>A fresh stream over the bytes on every call.</summary>
    public Stream OpenRead() => new MemoryStream(content, writable: false);
}

/// <summary>One choice of a select field: the value submitted and the label shown.</summary>
public sealed record AdminOption(string Value, string Label);
