using AdminForge.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace AdminForge.UiTests;

[Route("/admin/reports")]
public sealed class ReportsPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b) => b.AddContent(0, "reports");
}

[Route("/admin/audit-export")]
public sealed class AuditExportPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b) =>
        b.AddContent(0, "secret export");
}

/// <summary>Reports waits on <see cref="Reports"/>; audit export is refused at once.</summary>
public sealed class SlowPolicy : IAdminPolicy
{
    public static TaskCompletionSource<bool> Reports { get; } = new();

    public async ValueTask<bool> AllowsAsync(AdminRequest request, CancellationToken ct) =>
        request is not PageRequest page || page.PageId == "reports" && await Reports.Task;
}

public sealed class RouteTests : BunitContext
{
    public RouteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization();
        Services.AddLogging();
        Services.AddSingleton<IWebHostEnvironment, TestHost>();
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddAdminForge(p =>
            p.AllowAnonymous().Policy<SlowPolicy>().Page<ReportsPage>().Page<AuditExportPage>()
        );
        Services.AddMudServices();
        Services.AddSingleton<NavigationManager>(sp =>
            sp.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>()
        );
        Services.AddSingleton(JSInterop.JSRuntime);
        SetRendererInfo(new RendererInfo("Server", true));
    }

    [Fact]
    public void A_late_verdict_for_the_previous_page_never_opens_the_current_one()
    {
        var view = Render<PolicedRouteView>(ps =>
            ps.Add(
                v => v.RouteData,
                new RouteData(typeof(ReportsPage), new Dictionary<string, object?>())
            )
        );
        view.Render(ps =>
            ps.Add(
                v => v.RouteData,
                new RouteData(typeof(AuditExportPage), new Dictionary<string, object?>())
            )
        );
        view.WaitForElement("[data-af=refused]");

        var renders = view.RenderCount;
        view.InvokeAsync(() => SlowPolicy.Reports.SetResult(true));
        view.WaitForState(() => view.RenderCount > renders);

        Assert.DoesNotContain("secret export", view.Markup);
        Assert.NotNull(view.Find("[data-af=refused]"));
    }
}
