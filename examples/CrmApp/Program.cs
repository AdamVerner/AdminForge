namespace CrmApp;

/// <summary>Named rather than top-level so the assembly declares no global <c>Program</c>.</summary>
internal static class EntryPoint
{
    private static void Main(string[] args) => CrmHost.Create(args).Run();
}
