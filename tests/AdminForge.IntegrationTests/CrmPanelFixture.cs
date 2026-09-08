using CrmApp;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AdminForge.IntegrationTests;

/// <summary>
/// The <c>CrmApp</c> example, mounted on a test server. It has no DbContext, so what it exercises
/// is the provider-backed path end to end.
/// </summary>
public sealed class CrmPanelFixture : WebApplicationFactory<Organization>
{
    public HttpClient Client => field ??= CreateClient();
}
