namespace AdminForge.Core.Metadata;

/// <summary>How a string column's value is shown when opened; the cell itself shows a one-line preview.</summary>
public enum Content
{
    /// <summary>Plain text in a wrapped block.</summary>
    Text,
    Markdown,

    /// <summary>Re-indented; a value that is not JSON shows as text.</summary>
    Json,

    /// <summary>In a sandboxed frame: no scripts, no access to the panel.</summary>
    Html,
}
