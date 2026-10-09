using System.Collections.Concurrent;
using System.Security.Claims;
using AdminForge;
using TodoApp.Entities;

namespace TodoApp;

/// <summary>Accepts admin / admin. Five wrong passwords in a row lock the name for a minute.</summary>
public sealed class DemoCredentials : IAdminCredentialValidator
{
    // Static: the validator is scoped, the lockout outlives the request.
    private static readonly ConcurrentDictionary<
        string,
        (int Failures, DateTimeOffset Until)
    > Attempts = new();

    public Task<LoginResult> ValidateAsync(string username, string password, CancellationToken ct)
    {
        var (failures, until) = Attempts.GetValueOrDefault(username);
        if (until > DateTimeOffset.UtcNow)
            return Task.FromResult(LoginResult.Locked(until - DateTimeOffset.UtcNow));
        if (username != "admin" || password != "admin")
        {
            Attempts[username] =
                ++failures >= 5 ? (0, DateTimeOffset.UtcNow.AddMinutes(1)) : (failures, default);
            return Task.FromResult(LoginResult.Rejected);
        }
        Attempts.TryRemove(username, out _);
        return Task.FromResult(
            LoginResult.Ok(
                new ClaimsPrincipal(
                    new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, "admin"),
                        new Claim(ClaimTypes.Name, "admin"),
                    ])
                )
            )
        );
    }
}

/// <summary>A done task is frozen: it can be read, but not edited, deleted or acted on.</summary>
public sealed class FrozenWhenDonePolicy : IAdminPolicy
{
    public ValueTask<bool> AllowsAsync(AdminRequest request, CancellationToken ct) =>
        new(
            request switch
            {
                TableRequest<Todo> { Op: AdminOp.Update or AdminOp.Delete, Row: { } todo } =>
                    todo.Status != TodoStatus.Done,
                ActionRequest<Todo> { Row: { } todo } => todo.Status != TodoStatus.Done,
                _ => true,
            }
        );
}
