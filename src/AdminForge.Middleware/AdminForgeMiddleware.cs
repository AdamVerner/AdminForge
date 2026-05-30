using AdminForge.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Middleware;

/// <summary>
/// Request-pipeline middleware that gates AdminForge endpoints behind the
/// umbrella authorization policy configured on <see cref="AdminForgeOptions.AuthorizationPolicy"/>.
/// When no policy is configured the middleware is a no-op pass-through.
/// </summary>
public sealed class AdminForgeMiddleware
{
    private readonly RequestDelegate _next;

    public AdminForgeMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AdminForgeOptions options,
        IAuthorizationService authorizationService
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (
            !IsAdminPath(context.Request.Path, options.RoutePrefix)
            || string.IsNullOrWhiteSpace(options.AuthorizationPolicy)
        )
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var result = await authorizationService
            .AuthorizeAsync(context.User, resource: null, policyName: options.AuthorizationPolicy)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            context.Response.StatusCode =
                context.User.Identity?.IsAuthenticated == true
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized;
            return;
        }
        await _next(context).ConfigureAwait(false);
    }

    private static bool IsAdminPath(PathString path, string prefix)
    {
        if (!path.HasValue)
            return false;
        var normalised = "/" + prefix.Trim('/');
        return path.StartsWithSegments(normalised, StringComparison.OrdinalIgnoreCase);
    }
}
