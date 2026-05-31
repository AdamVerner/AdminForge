using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent composer for a single generic form. Mirrors the dashboard builder
/// shape: fields accumulate in registration order, nav is configured via a
/// callback, and the user supplies a submit handler that runs in a fresh DI
/// scope on success.
/// </summary>
public sealed class FormBuilder
{
    private readonly string _routeName;
    private string _title;
    private string? _description;
    private readonly NavMeta _nav = new();
    private readonly List<FieldMeta> _fields = [];
    private Func<IServiceProvider, FormSubmission, IActionContext, Task>? _submit;

    internal FormBuilder(string routeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        _routeName = routeName;
        _title = routeName;
    }

    /// <summary>Sets the human-readable title shown above the form.</summary>
    public FormBuilder WithTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _title = title;
        return this;
    }

    /// <summary>Sets the optional helper text rendered below the title.</summary>
    public FormBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Customise the sidebar nav entry (label, group, order, hidden).</summary>
    public FormBuilder Nav(Action<NavBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new NavBuilder(_nav));
        return this;
    }

    /// <summary>
    /// Declares a field. The callback receives a <see cref="FieldBuilder"/>;
    /// it must invoke exactly one kind method (e.g. <c>f.Text("Name")</c>) and
    /// may chain typed overrides afterwards.
    /// </summary>
    public FormBuilder AddField(Action<FieldBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new FieldBuilder();
        configure(b);
        var meta = b.Build();
        if (_fields.Any(f => string.Equals(f.Name, meta.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Field '{meta.Name}' is already declared on form '{_routeName}'."
            );
        }
        _fields.Add(meta);
        return this;
    }

    /// <summary>Registers the submit handler. Required — building a form without one throws.</summary>
    public FormBuilder OnSubmit(
        Func<IServiceProvider, FormSubmission, IActionContext, Task> handler
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        _submit = handler;
        return this;
    }

    internal FormMeta Build()
    {
        var meta = new FormMeta
        {
            RouteName = _routeName,
            Title = _title,
            Description = _description,
            Nav = _nav,
            Submit = _submit,
        };
        foreach (var f in _fields)
            meta.Fields.Add(f);
        return meta;
    }
}
