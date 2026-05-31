namespace AdminForge.Core.Configuration;

/// <summary>
/// Thrown by the bridge when a form submission fails validation
/// (missing required field or a user-supplied validator rejected the value).
/// Carries per-field error messages so the renderer can surface them inline.
/// </summary>
public sealed class FormValidationException : Exception
{
    public FormValidationException(string formRouteName, IDictionary<string, string> errors)
        : base($"Form '{formRouteName}' failed validation ({errors.Count} error(s)).")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formRouteName);
        ArgumentNullException.ThrowIfNull(errors);
        FormRouteName = formRouteName;
        Errors = new Dictionary<string, string>(errors, StringComparer.Ordinal);
    }

    public string FormRouteName { get; }

    /// <summary>Field name → human-readable error message.</summary>
    public IReadOnlyDictionary<string, string> Errors { get; }
}
