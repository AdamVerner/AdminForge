using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

/// <summary>
/// Builder-shape tests for <c>EntityBuilder&lt;T&gt;.OnUpdate(...)</c> and the
/// <see cref="UpdateResult"/> discriminated record. Mirrors
/// <see cref="CustomCreateHandlerBuilderTests"/>; runtime dispatch is covered in
/// the integration suite.
/// </summary>
public class CustomUpdateHandlerBuilderTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void OnUpdate_Stores_Handler_On_EntityMeta()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e =>
            e.OnUpdate((_, _, _, _, _) => Task.FromResult(UpdateResult.Ok()))
        );

        var meta = builder.Build().Entities.Single();
        Assert.NotNull(meta.CustomUpdateHandler);
    }

    [Fact]
    public void OnUpdate_Throws_When_Registered_Twice()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddTable<User>(e =>
                e.OnUpdate((_, _, _, _, _) => Task.FromResult(UpdateResult.Ok()))
                    .OnUpdate((_, _, _, _, _) => Task.FromResult(UpdateResult.Ok()))
            )
        );
    }

    [Fact]
    public void Without_OnUpdate_CustomUpdateHandler_Is_Null()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>();
        var meta = builder.Build().Entities.Single();
        Assert.Null(meta.CustomUpdateHandler);
    }

    [Fact]
    public async Task OnUpdate_Adapter_Forwards_Typed_Instances()
    {
        User? capturedOriginal = null;
        User? capturedPatched = null;
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e =>
            e.OnUpdate(
                (_, orig, patched, _, _) =>
                {
                    capturedOriginal = orig;
                    capturedPatched = patched;
                    return Task.FromResult(UpdateResult.Ok());
                }
            )
        );

        var meta = builder.Build().Entities.Single();
        var orig = new User
        {
            Id = 1,
            DisplayName = "orig",
            Email = "o@x.y",
        };
        var patched = new User
        {
            Id = 1,
            DisplayName = "patched",
            Email = "p@x.y",
        };
        var result = await meta.CustomUpdateHandler!(
            null!,
            orig,
            patched,
            new NullActionContext(),
            CancellationToken.None
        );

        Assert.Same(orig, capturedOriginal);
        Assert.Same(patched, capturedPatched);
        Assert.IsType<UpdateResult.Success>(result);
    }

    [Fact]
    public void UpdateResult_Ok_Returns_Success()
    {
        var result = UpdateResult.Ok();
        Assert.IsType<UpdateResult.Success>(result);
    }

    [Fact]
    public void UpdateResult_Error_Returns_Failure_With_Message()
    {
        var result = UpdateResult.Error("nope");
        var failure = Assert.IsType<UpdateResult.Failure>(result);
        Assert.Equal("nope", failure.Message);
    }

    [Fact]
    public void UpdateResult_Records_Are_Value_Equal()
    {
        Assert.Equal(new UpdateResult.Success(), new UpdateResult.Success());
        Assert.Equal(new UpdateResult.Failure("x"), new UpdateResult.Failure("x"));
        Assert.NotEqual(new UpdateResult.Failure("x"), new UpdateResult.Failure("y"));
    }

    private sealed class NullActionContext : IActionContext
    {
        public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);

        public void ShowSuccess(string message) { }

        public void ShowError(string message) { }

        public void NavigateTo(string url) { }

        public void Refresh() { }
    }
}
