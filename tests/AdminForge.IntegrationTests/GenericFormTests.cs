using System.Net;
using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Data;
using TodoApp.Entities;

namespace AdminForge.IntegrationTests;

/// <summary>
/// End-to-end coverage of Phase 4 generic forms: the page renders, the bridge
/// validates + emits audit on submit, denying authz short-circuits before the
/// handler, and missing required fields surface as <see cref="FormValidationException"/>.
/// </summary>
public class GenericFormTests : IClassFixture<FormTodoAppFactory>
{
    private readonly FormTodoAppFactory _factory;

    public GenericFormTests(FormTodoAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Form_Page_Renders_Title_And_Field_Labels()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/admin/forms/send-notification");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();

        Assert.Contains("Send Notification", body);
        // Each of the 8 field kinds contributes a label to the rendered HTML.
        Assert.Contains("Title", body);
        Assert.Contains("Body", body);
        Assert.Contains("Rich Body", body);
        Assert.Contains("Priority", body);
        Assert.Contains("Amplification Factor", body);
        Assert.Contains("Urgent", body);
        Assert.Contains("Scheduled Date", body);
        Assert.Contains("Expires At", body);
        Assert.Contains("Attachment", body);
    }

    [Fact]
    public async Task Unknown_Form_Renders_Error_Inline()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/admin/forms/does-not-exist");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Unknown form", body);
    }

    [Fact]
    public async Task Bridge_SubmitForm_Runs_Handler_And_Emits_Audit()
    {
        _factory.AuditSink.Events.Clear();
        _factory.HandlerCalls.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var sub = new FormSubmission(
            new Dictionary<string, object?>
            {
                ["Title"] = "Hello",
                ["Body"] = "World",
                ["Priority"] = 3L,
            }
        );
        await bridge.SubmitFormAsync("send-notification", sub, context: null);

        Assert.Single(_factory.HandlerCalls);
        Assert.Equal("Hello", _factory.HandlerCalls[0]);

        var evt = Assert.Single(_factory.AuditSink.Events);
        Assert.Equal(AuditAction.FormSubmit, evt.Action);
        Assert.Equal("Form:send-notification", evt.EntityType);
        Assert.Null(evt.EntityId);
        Assert.Equal("Hello", evt.ChangedValues["Title"].NewValue);
        Assert.Equal(3L, evt.ChangedValues["Priority"].NewValue);
    }

    [Fact]
    public async Task Bridge_SubmitForm_Throws_When_Required_Field_Missing()
    {
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var sub = new FormSubmission(
            new Dictionary<string, object?> { ["Body"] = "x" } // Title omitted
        );
        var ex = await Assert.ThrowsAsync<FormValidationException>(() =>
            bridge.SubmitFormAsync("send-notification", sub, context: null)
        );
        Assert.True(ex.Errors.ContainsKey("Title"));
    }

    [Fact]
    public async Task Bridge_SubmitForm_File_Audit_Summarises_Not_Dumps_Bytes()
    {
        _factory.AuditSink.Events.Clear();
        _factory.HandlerCalls.Clear();

        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var sub = new FormSubmission(
            new Dictionary<string, object?> { ["Title"] = "with-file", ["Body"] = "x" },
            new Dictionary<string, FormFileUpload>
            {
                ["Attachment"] = new FormFileUpload(
                    "note.pdf",
                    "application/pdf",
                    new byte[] { 1, 2, 3 }
                ),
            }
        );
        await bridge.SubmitFormAsync("send-notification", sub, context: null);

        var evt = Assert.Single(_factory.AuditSink.Events);
        var attachment = evt.ChangedValues["Attachment"].NewValue;
        Assert.NotNull(attachment);
        var dict = Assert.IsType<Dictionary<string, object?>>(attachment);
        Assert.Equal("note.pdf", dict["FileName"]);
        Assert.Equal("application/pdf", dict["ContentType"]);
        Assert.Equal(3L, dict["Length"]);
    }

    [Fact]
    public async Task Bridge_SubmitForm_Throws_AdminForbidden_When_Policy_Denies()
    {
        await using var deny = new DenyingFormFactory(_factory.HandlerCalls, _factory.AuditSink);
        using var scope = deny.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IAdminUIBridge>();
        var sub = new FormSubmission(
            new Dictionary<string, object?> { ["Title"] = "x", ["Body"] = "y" }
        );
        await Assert.ThrowsAsync<AdminForbiddenException>(() =>
            bridge.SubmitFormAsync("send-notification", sub, context: null)
        );
    }
}

/// <summary>
/// Forks TodoApp with a captured invocation log + audit sink so tests can inspect
/// the handler effect and audit payload.
/// </summary>
public class FormTodoAppFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; } = new();
    public List<string> HandlerCalls { get; } = new();

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-form-{Guid.NewGuid():N}.db"
    );

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services => Configure(services, HandlerCalls, AuditSink));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch { }
    }

    internal static void Configure(
        IServiceCollection services,
        List<string> handlerCalls,
        CapturingAuditSink sink
    )
    {
        services.RemoveAll<AdminForgeOptions>();
        services.AddSingleton(sp =>
        {
            using var scope = sp.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var scanner = sp.GetRequiredService<EfCoreReflectionScanner>();
            var scanned = scanner.Scan(ctx);

            var b = new AdminForgeBuilder(scanned);
            b.WithTitle("Form Tests")
                .WithAuditLog(sink)
                .AddTable<User>()
                .AddTable<TodoList>()
                .AddTable<Todo>()
                .AddTable<Tag>()
                .AddForm(
                    "send-notification",
                    f =>
                        f.WithTitle("Send Notification")
                            .Nav(n => n.Group("Tools"))
                            .AddField(x => x.Text("Title").Required())
                            .AddField(x => x.Text("Body").Multiline().MaxLength(1000).Required())
                            .AddField(x => x.Markdown("RichBody").Label("Rich Body"))
                            .AddField(x => x.Number("Priority").Min(0).Max(5))
                            .AddField(x =>
                                x.Float("AmplificationFactor").Label("Amplification Factor")
                            )
                            .AddField(x => x.Bool("Urgent"))
                            .AddField(x => x.Date("ScheduledDate").Label("Scheduled Date"))
                            .AddField(x => x.DateTime("ExpiresAt").Label("Expires At"))
                            .AddField(x => x.FileUpload("Attachment").MaxSizeBytes(5_000_000))
                            .OnSubmit(
                                (sp, submission, ctx) =>
                                {
                                    handlerCalls.Add(submission.Get<string>("Title") ?? "");
                                    return Task.CompletedTask;
                                }
                            )
                );
            return b.Build();
        });
    }
}

/// <summary>
/// Forks the form host with a deny-all policy on <see cref="AdminAction.FormSubmit"/>.
/// </summary>
public class DenyingFormFactory : WebApplicationFactory<Program>
{
    public CapturingAuditSink AuditSink { get; }
    public List<string> HandlerCalls { get; }

    public readonly string DbPath = Path.Combine(
        Path.GetTempPath(),
        $"adminforge-form-deny-{Guid.NewGuid():N}.db"
    );

    public DenyingFormFactory(List<string> calls, CapturingAuditSink sink)
    {
        HandlerCalls = calls;
        AuditSink = sink;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.ConfigureServices(services =>
        {
            FormTodoAppFactory.Configure(services, HandlerCalls, AuditSink);
            services.AddSingleton<IAdminAuthorizationPolicy, DenyFormSubmit>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (File.Exists(DbPath))
                File.Delete(DbPath);
        }
        catch { }
    }

    private sealed class DenyFormSubmit : IAdminAuthorizationPolicy
    {
        public Task<bool> IsAuthorizedAsync(
            string entityName,
            AdminAction action,
            ClaimsPrincipal user,
            object? instance = null,
            string? actionName = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(action != AdminAction.FormSubmit);
    }
}
