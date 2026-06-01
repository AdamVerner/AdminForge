using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

/// <summary>
/// Builder-shape tests for <c>EntityBuilder&lt;T&gt;.OnCreate(...)</c> and the
/// <see cref="CreateResult"/> discriminated record. Runtime dispatch behaviour
/// (bridge wiring, audit emission, exception surfacing) is exercised in the
/// integration test suite.
/// </summary>
public class CustomCreateHandlerBuilderTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void OnCreate_Stores_Handler_On_EntityMeta()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e =>
            e.OnCreate((_, _, _, _) => Task.FromResult(CreateResult.Ok(1)))
        );

        var meta = builder.Build().Entities.Single();
        Assert.NotNull(meta.CustomCreateHandler);
    }

    [Fact]
    public async Task OnCreate_Adapter_Forwards_Typed_Instance_To_User_Handler()
    {
        // The builder wraps the typed Func<sp,T,ctx,ct,Task<CreateResult>> in an
        // object-accepting adapter — exercise it directly to confirm the cast.
        User? captured = null;
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e =>
            e.OnCreate(
                (_, u, _, _) =>
                {
                    captured = u;
                    return Task.FromResult(CreateResult.Ok(u.Id));
                }
            )
        );

        var meta = builder.Build().Entities.Single();
        var input = new User
        {
            Id = 7,
            DisplayName = "x",
            Email = "x@y.z",
        };
        var result = await meta.CustomCreateHandler!(
            null!,
            input,
            new NullActionContext(),
            CancellationToken.None
        );

        Assert.Same(input, captured);
        var success = Assert.IsType<CreateResult.Success>(result);
        Assert.Equal(7, success.Id);
    }

    [Fact]
    public void OnCreate_Throws_When_Registered_Twice()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddTable<User>(e =>
                e.OnCreate((_, _, _, _) => Task.FromResult(CreateResult.Ok(1)))
                    .OnCreate((_, _, _, _) => Task.FromResult(CreateResult.Ok(2)))
            )
        );
    }

    [Fact]
    public void Without_OnCreate_CustomCreateHandler_Is_Null()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>();
        var meta = builder.Build().Entities.Single();
        Assert.Null(meta.CustomCreateHandler);
    }

    [Fact]
    public void CreateResult_Ok_Returns_Success_With_Id()
    {
        var result = CreateResult.Ok(42);
        var success = Assert.IsType<CreateResult.Success>(result);
        Assert.Equal(42, success.Id);
    }

    [Fact]
    public void CreateResult_Error_Returns_Failure_With_Message()
    {
        var result = CreateResult.Error("nope");
        var failure = Assert.IsType<CreateResult.Failure>(result);
        Assert.Equal("nope", failure.Message);
    }

    [Fact]
    public void CreateResult_Success_Records_Are_Value_Equal()
    {
        Assert.Equal(new CreateResult.Success(1), new CreateResult.Success(1));
        Assert.NotEqual(new CreateResult.Success(1), new CreateResult.Success(2));
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
