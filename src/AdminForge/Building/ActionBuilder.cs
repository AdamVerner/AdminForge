using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

internal sealed class ActionDraft(string id, string title)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string? Confirm { get; set; }
    public Tone Tone { get; set; }
    public Type? InputType { get; set; }
    public Dictionary<string, FieldSettings> Fields { get; } = new(StringComparer.Ordinal);
    public Func<object, bool>? When { get; set; }
    public Func<IServiceProvider, object, CancellationToken, Task<object>>? Prefill { get; set; }
    public Func<
        IServiceProvider,
        object,
        object?,
        ClaimsPrincipal,
        CancellationToken,
        Task<AdminContext>
    >? Run { get; set; }

    public ActionModel Build(string table) =>
        new()
        {
            Id = Id,
            Title = Title,
            Confirm = Confirm,
            Tone = Tone,
            Input = InputType is null ? null : AdminForge.Fields.Input(InputType, Fields),
            When = When,
            Prefill = Prefill,
            Run =
                Run
                ?? throw new InvalidOperationException(
                    $"Action '{Id}' on table '{table}' has no Run(...)."
                ),
        };
}

/// <summary>An action on one row. The panel loads and authorizes the row before the handler runs.</summary>
public sealed class ActionBuilder<T>
    where T : class
{
    private readonly ActionDraft _draft;

    internal ActionBuilder(string id, string title) => _draft = new ActionDraft(id, title);

    /// <summary>The panel asks with <paramref name="message"/> before it runs the action.</summary>
    public ActionBuilder<T> Confirm(string message)
    {
        _draft.Confirm = message;
        return this;
    }

    public ActionBuilder<T> Tone(Tone tone)
    {
        _draft.Tone = tone;
        return this;
    }

    /// <summary>Offers the action only on rows that pass <paramref name="applies"/>, such as Activate on an inactive row.</summary>
    public ActionBuilder<T> When(Func<T, bool> applies)
    {
        _draft.When = row => applies((T)row);
        return this;
    }

    /// <summary>Asks for a <typeparamref name="TInput"/> first; its properties are the dialog's fields.</summary>
    public ActionBuilder<T, TInput> Input<TInput>()
        where TInput : class
    {
        _draft.InputType = typeof(TInput);
        return new ActionBuilder<T, TInput>(_draft);
    }

    public ActionBuilder<T> Run(Func<ActionContext<T>, Task> handler) =>
        Run<IServiceProvider>((_, ctx) => handler(ctx));

    public ActionBuilder<T> Run(Action<ActionContext<T>> handler) =>
        Run(ctx =>
        {
            handler(ctx);
            return Task.CompletedTask;
        });

    /// <summary>Runs with <typeparamref name="TService"/> from the operation's scope, the one that loaded the row.</summary>
    public ActionBuilder<T> Run<TService>(Func<TService, ActionContext<T>, Task> handler)
        where TService : notnull
    {
        _draft.Run = async (sp, row, _, user, ct) =>
        {
            var ctx = new ActionContext<T>((T)row, user, ct);
            await handler(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), ctx)
                .ConfigureAwait(false);
            return ctx;
        };
        return this;
    }

    internal ActionModel Build(string table) => _draft.Build(table);
}

/// <summary>An action that asks for a <typeparamref name="TInput"/> before it runs.</summary>
public sealed class ActionBuilder<T, TInput>
    where T : class
    where TInput : class
{
    private readonly ActionDraft _draft;

    internal ActionBuilder(ActionDraft draft) => _draft = draft;

    public ActionBuilder<T, TInput> Confirm(string message)
    {
        _draft.Confirm = message;
        return this;
    }

    public ActionBuilder<T, TInput> Tone(Tone tone)
    {
        _draft.Tone = tone;
        return this;
    }

    /// <inheritdoc cref="ActionBuilder{T}.When"/>
    public ActionBuilder<T, TInput> When(Func<T, bool> applies)
    {
        _draft.When = row => applies((T)row);
        return this;
    }

    /// <summary>The input the dialog opens with, from the row, such as the permissions a role has now.</summary>
    public ActionBuilder<T, TInput> Prefill(Func<T, TInput> input)
    {
        _draft.Prefill = (_, row, _) => Task.FromResult<object>(input((T)row));
        return this;
    }

    /// <summary>The input the dialog opens with, from <typeparamref name="TService"/> and the row.</summary>
    public ActionBuilder<T, TInput> Prefill<TService>(
        Func<TService, T, CancellationToken, Task<TInput>> input
    )
        where TService : notnull
    {
        _draft.Prefill = async (sp, row, ct) =>
            await input(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), (T)row, ct)
                .ConfigureAwait(false);
        return this;
    }

    /// <summary>Choices for <paramref name="field"/> from <typeparamref name="TService"/> and the row, loaded when the dialog opens and again on submit.</summary>
    /// <remarks>Over MCP the tool cannot list them before a row is named; a call is checked against them.</remarks>
    public ActionBuilder<T, TInput> Options<TService>(
        Expression<Func<TInput, object?>> field,
        Func<TService, T, CancellationToken, Task<IReadOnlyList<AdminOption>>> options
    )
        where TService : notnull
    {
        FieldSettings.Of(_draft.Fields, Members.Name(field)).RowOptions = (sp, row, ct) =>
            options(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), (T)row, ct);
        return this;
    }

    /// <summary>Configures what an input's type and attributes cannot say: choices, Markdown, accepted files.</summary>
    public ActionBuilder<T, TInput> Field<TProp>(
        Expression<Func<TInput, TProp>> field,
        Action<FieldBuilder<TProp>> configure
    )
    {
        configure(new FieldBuilder<TProp>(FieldSettings.Of(_draft.Fields, Members.Name(field))));
        return this;
    }

    public ActionBuilder<T, TInput> Run(Func<ActionContext<T, TInput>, Task> handler) =>
        Run<IServiceProvider>((_, ctx) => handler(ctx));

    public ActionBuilder<T, TInput> Run(Action<ActionContext<T, TInput>> handler) =>
        Run(ctx =>
        {
            handler(ctx);
            return Task.CompletedTask;
        });

    public ActionBuilder<T, TInput> Run<TService>(
        Func<TService, ActionContext<T, TInput>, Task> handler
    )
        where TService : notnull
    {
        _draft.Run = async (sp, row, input, user, ct) =>
        {
            var ctx = new ActionContext<T, TInput>((T)row, (TInput)input!, user, ct);
            await handler(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), ctx)
                .ConfigureAwait(false);
            return ctx;
        };
        return this;
    }
}

/// <summary>What an input record's property type cannot say about its field.</summary>
public sealed class FieldBuilder<TProp>
{
    private readonly FieldSettings _s;

    internal FieldBuilder(FieldSettings settings) => _s = settings;

    public FieldBuilder<TProp> Title(string title)
    {
        _s.Title = title;
        return this;
    }

    public FieldBuilder<TProp> Description(string description)
    {
        _s.Description = description;
        return this;
    }

    /// <summary>A Markdown editor with a preview.</summary>
    public FieldBuilder<TProp> Markdown()
    {
        _s.Markdown = true;
        return this;
    }

    public FieldBuilder<TProp> Multiline()
    {
        _s.Multiline = true;
        return this;
    }

    /// <summary>A fixed list of choices, each its own label. A list-of-strings property takes several.</summary>
    public FieldBuilder<TProp> Options(params string[] values)
    {
        IReadOnlyList<AdminOption> options = values.Select(v => new AdminOption(v, v)).ToList();
        _s.Options = (_, _) => Task.FromResult(options);
        return this;
    }

    /// <summary>Choices from <typeparamref name="TService"/>, loaded when the field shows and again when it is submitted.</summary>
    public FieldBuilder<TProp> Options<TService>(
        Func<TService, CancellationToken, Task<IReadOnlyList<AdminOption>>> options
    )
        where TService : notnull
    {
        _s.Options = (sp, ct) =>
            options(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), ct);
        return this;
    }

    /// <summary>The extensions an upload accepts, such as <c>.pdf</c>.</summary>
    public FieldBuilder<TProp> Accept(params string[] extensions)
    {
        _s.Accept = extensions
            .Select(e => e.Trim().ToLowerInvariant())
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToList();
        return this;
    }

    public FieldBuilder<TProp> MaxBytes(long bytes)
    {
        _s.MaxBytes = bytes;
        return this;
    }

    public FieldBuilder<TProp> Validate(Func<TProp, bool> rule, string message)
    {
        _s.Rules.Add(v => v is TProp value && !rule(value) ? message : null);
        return this;
    }
}

/// <summary>A form of its own, under <c>/admin/forms/{id}</c>, whose fields are <typeparamref name="TInput"/>'s properties.</summary>
public sealed class FormBuilder<TInput>
    where TInput : class
{
    private readonly string _id;
    private readonly Dictionary<string, FieldSettings> _fields = new(StringComparer.Ordinal);
    private NavModel _nav;
    private string _title;
    private string? _description;
    private Func<
        IServiceProvider,
        object,
        ClaimsPrincipal,
        CancellationToken,
        Task<AdminContext>
    >? _run;

    internal FormBuilder(string id, NavModel nav)
    {
        _id = id;
        _nav = nav;
        _title = Members.Humanize(id.Replace('-', ' '));
    }

    public FormBuilder<TInput> Title(string title)
    {
        _title = title;
        return this;
    }

    public FormBuilder<TInput> Description(string description)
    {
        _description = description;
        return this;
    }

    public FormBuilder<TInput> Area(NavArea area)
    {
        _nav = _nav with { Area = area };
        return this;
    }

    public FormBuilder<TInput> Field<TProp>(
        Expression<Func<TInput, TProp>> field,
        Action<FieldBuilder<TProp>> configure
    )
    {
        configure(new FieldBuilder<TProp>(FieldSettings.Of(_fields, Members.Name(field))));
        return this;
    }

    public FormBuilder<TInput> Run(Func<FormContext<TInput>, Task> handler) =>
        Run<IServiceProvider>((_, ctx) => handler(ctx));

    public FormBuilder<TInput> Run(Action<FormContext<TInput>> handler) =>
        Run(ctx =>
        {
            handler(ctx);
            return Task.CompletedTask;
        });

    public FormBuilder<TInput> Run<TService>(Func<TService, FormContext<TInput>, Task> handler)
        where TService : notnull
    {
        _run = async (sp, input, user, ct) =>
        {
            var ctx = new FormContext<TInput>((TInput)input, user, ct);
            await handler(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), ctx)
                .ConfigureAwait(false);
            return ctx;
        };
        return this;
    }

    internal FormModel Build() =>
        new()
        {
            Id = _id,
            Title = _title,
            Description = _description,
            Nav = _nav,
            Input = AdminForge.Fields.Input(typeof(TInput), _fields),
            Run = _run ?? throw new InvalidOperationException($"Form '{_id}' has no Run(...)."),
        };
}
