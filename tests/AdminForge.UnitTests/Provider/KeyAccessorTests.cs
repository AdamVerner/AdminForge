using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Provider;

public class KeyAccessorTests
{
    [Fact]
    public void Encodes_And_Decodes_Single_Int_Key()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        var accessor = new KeyAccessor(ctx.Model.FindEntityType(typeof(Todo))!);

        var encoded = accessor.EncodeKeyValues([42]);
        Assert.Equal("42", encoded);

        var decoded = accessor.DecodeKey(encoded);
        Assert.Single(decoded);
        Assert.Equal(42, decoded[0]);
    }

    /// <summary>A Guid or a hyphenated string is one part; the separator never occurs inside an escaped part.</summary>
    [Fact]
    public void Composite_Key_Parts_Survive_Their_Own_Hyphens()
    {
        var accessor = new KeyAccessor(
            typeof(Membership),
            [nameof(Membership.OrgId), nameof(Membership.Id)]
        );
        var id = Guid.Parse("1e7537b9-b007-4679-87b9-595410157120");

        var encoded = accessor.EncodeKey(new Membership(5, id));

        Assert.Equal("5,1e7537b9-b007-4679-87b9-595410157120", encoded);
        Assert.Equal([5, id], accessor.DecodeKey(encoded));
        Assert.Equal(
            ["a-b", 5],
            new KeyAccessor(typeof(Tagged), [nameof(Tagged.Code), nameof(Tagged.N)]).DecodeKey(
                new KeyAccessor(typeof(Tagged), [nameof(Tagged.Code), nameof(Tagged.N)]).EncodeKey(
                    new Tagged("a-b", 5)
                )
            )
        );
    }

    private sealed record Membership(int OrgId, Guid Id);

    private sealed record Tagged(string Code, int N);

    [Fact]
    public void GetKeyValues_Reads_From_Instance()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        var accessor = new KeyAccessor(ctx.Model.FindEntityType(typeof(Todo))!);

        var values = accessor.GetKeyValues(new Todo { Id = 7, Title = "x" });
        Assert.Single(values);
        Assert.Equal(7, values[0]);
    }

    [Fact]
    public void Throws_On_Mismatched_Key_Arity()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        var accessor = new KeyAccessor(ctx.Model.FindEntityType(typeof(Todo))!);

        Assert.Throws<ArgumentException>(() => accessor.EncodeKeyValues([1, 2]));
        Assert.Throws<ArgumentException>(() => accessor.DecodeKey("1,2"));
    }
}
