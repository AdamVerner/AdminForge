using AdminForge;

namespace AdminForge.UnitTests;

public class AdminForgeOptionsTests
{
    [Fact]
    public void DefaultOptions_HaveExpectedValues()
    {
        var options = new AdminForgeOptions();
        Assert.Equal("admin", options.RoutePrefix);
        Assert.Equal("Admin", options.Title);
    }
}
