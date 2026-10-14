using AdminForge.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace AdminForge;

public static class AdminForgeExtensions
{
    /// <summary>Registers the panel. <paramref name="configure"/> runs now; the model it describes is built and checked by <c>MapAdminForge()</c>.</summary>
    public static IServiceCollection AddAdminForge(
        this IServiceCollection services,
        Action<PanelBuilder> configure
    )
    {
        if (services.Any(d => d.ServiceType == typeof(PanelBuilder)))
            throw new InvalidOperationException(
                "AddAdminForge is called twice; one panel per host."
            );
        var panel = new PanelBuilder(services);
        configure(panel);
        services.AddSingleton(panel);
        services.AddSingleton(sp => sp.GetRequiredService<PanelBuilder>().Build(sp));
        services.AddScoped<AdminPanel>();
        services.AddScoped<Missing>();
        services.AddScoped<OperationRunner>();
        services.AddScoped<OperationUserAccessor>();
        services.AddScoped<IUserAccessor, CurrentUserAccessor>();
        services.AddHttpContextAccessor();
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddMudServices();
        services.AddAntiforgery();
        services.AddAuthorization();
        return services;
    }

    private static readonly string[] PanelAssetPrefixes =
    [
        "_content/AdminForge/",
        "_content/MudBlazor/",
        "_framework/",
    ];

    /// <summary>
    /// Mounts the panel at <c>/admin</c>, with its login and MCP endpoints when configured. Call
    /// after <c>UseAuthentication()</c> and <c>UseAuthorization()</c>. Fails here, at startup, on a
    /// configuration the panel cannot serve.
    /// </summary>
    public static IEndpointConventionBuilder MapAdminForge(this IEndpointRouteBuilder endpoints)
    {
        var builder =
            endpoints.ServiceProvider.GetService<PanelBuilder>()
            ?? throw new InvalidOperationException(
                "AdminForge is not registered. Call services.AddAdminForge(...) first."
            );
        var panel = endpoints.ServiceProvider.GetRequiredService<PanelModel>();
        if (panel.Policy is null == !panel.AllowAnonymous)
            throw new InvalidOperationException(
                panel.AllowAnonymous
                    ? $"The panel both requires '{panel.Policy}' and allows anonymous access; pick one."
                    : "AdminForge refuses to mount without authorization: the panel reads and writes every table. "
                        + "Call RequireAuthorization(\"policy\") or UseLogin<TValidator>() on the panel, or "
                        + "AllowAnonymous() if it is meant to be open (a demo, local tooling, tests)."
            );

        // Form posts and the circuit's endpoints need it; the panel adds it once, here.
        if (endpoints is IApplicationBuilder app)
            app.UseAntiforgery();

        // The panel's own scripts are anonymous, or a host fallback policy would challenge the login page's; the host's stay under its policy.
        endpoints
            .MapStaticAssets()
            .Add(e =>
            {
                if (
                    e is RouteEndpointBuilder { RoutePattern.RawText: { } route }
                    && PanelAssetPrefixes.Any(route.TrimStart('/').StartsWith)
                )
                    e.Metadata.Add(new AllowAnonymousAttribute());
            });
        // The panel's endpoints are its own, not part of the host's API, so they stay out of its OpenAPI document.
        var own = endpoints.MapGroup("");
        own.ExcludeFromDescription();
        foreach (var map in builder.Endpoints)
            map(own, panel);

        var components = endpoints
            .MapRazorComponents<App>()
            .AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = "'none'")
            .AddAdditionalAssemblies([.. panel.HostAssemblies]);

        // On the endpoints the host's scheme decides what a refused request gets: a cookie
        // scheme redirects to its login, a bearer scheme answers 401.
        // An unknown panel path renders the shell's not-found page; the router answers it.
        var notFound = endpoints.MapFallback(
            $"{PanelModel.Root}/{{**path}}",
            () => new RazorComponentResult<App> { StatusCode = StatusCodes.Status404NotFound }
        ).ExcludeFromDescription();
        if (panel.Policy is not null)
        {
            components.RequireAuthorization(panel.Policy);
            notFound.RequireAuthorization(panel.Policy);
        }
        return components;
    }
}
