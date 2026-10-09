using System.Security.Claims;

namespace AdminForge;

/// <summary>What a handler receives: who runs it, its cancellation, and where it reports back.</summary>
public abstract class AdminContext
{
    private protected AdminContext(ClaimsPrincipal user, CancellationToken ct)
    {
        User = user;
        CancellationToken = ct;
    }

    public ClaimsPrincipal User { get; }
    public CancellationToken CancellationToken { get; }

    internal string? Message { get; private set; }
    internal string? Markdown { get; private set; }

    /// <summary>A one-line confirmation: a toast in the panel, the tool's text over MCP.</summary>
    public void Success(string message) => Message = message;

    /// <summary>A Markdown document shown as the operation's result.</summary>
    public void Result(string markdown) => Markdown = markdown;
}

/// <summary>An action's context: the row it runs on, loaded and authorized by the panel.</summary>
public class ActionContext<T> : AdminContext
{
    internal ActionContext(T row, ClaimsPrincipal user, CancellationToken ct)
        : base(user, ct) => Row = row;

    public T Row { get; }
}

/// <summary>An action's context with the input the admin submitted, validated.</summary>
public sealed class ActionContext<T, TInput> : ActionContext<T>
{
    internal ActionContext(T row, TInput input, ClaimsPrincipal user, CancellationToken ct)
        : base(row, user, ct) => Input = input;

    public TInput Input { get; }
}

/// <summary>A form's context with the input the admin submitted, validated.</summary>
public sealed class FormContext<TInput> : AdminContext
{
    internal FormContext(TInput input, ClaimsPrincipal user, CancellationToken ct)
        : base(user, ct) => Input = input;

    public TInput Input { get; }
}
