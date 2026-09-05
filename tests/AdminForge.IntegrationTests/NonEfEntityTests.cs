using System.Net;
using AdminForge.Core.Metadata;
using Microsoft.Extensions.DependencyInjection;
using TodoApp;

namespace AdminForge.IntegrationTests;

/// <summary>
/// A type off the DbContext is served by the provider the host registers, and a read-only one
/// offers nothing to create or edit.
/// </summary>
public class NonEfEntityTests : IClassFixture<TodoAppFactory>
{
    private readonly TodoAppFactory _todo;

    public NonEfEntityTests(TodoAppFactory todo) => _todo = todo;

    [Fact]
    public async Task A_Provider_Backed_Entity_Lists_Shows_And_Offers_Editing()
    {
        var client = _todo.CreateClient();

        var list = await client.GetAsync("/admin/entities/SiteSettings");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Contains("Welcome to Todo Admin!", listBody);
        Assert.Contains("New Site Settings", listBody);

        var view = await client.GetAsync("/admin/entities/SiteSettings/1");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Contains("Welcome to Todo Admin!", await view.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_Read_Only_Entity_Has_No_Create_Or_Edit_Surface()
    {
        _todo
            .Services.GetRequiredService<AuditLogStore>()
            .Record(
                new AuditEvent
                {
                    EntityType = "Probe",
                    Action = AuditAction.Custom,
                    User = "tester",
                }
            );
        var client = _todo.CreateClient();

        var listBody = await client.GetStringAsync("/admin/entities/AuditLogEntry");
        Assert.Contains("Probe", listBody);
        Assert.DoesNotContain("New Audit Log", listBody);

        var viewBody = await client.GetStringAsync("/admin/entities/AuditLogEntry/1");
        Assert.Contains("tester", viewBody);
        Assert.DoesNotContain(">Edit<", viewBody);

        Assert.Contains(
            "is read-only",
            await client.GetStringAsync("/admin/entities/AuditLogEntry/1/edit")
        );
        Assert.Contains(
            "is read-only",
            await client.GetStringAsync("/admin/entities/AuditLogEntry/new")
        );
    }
}
