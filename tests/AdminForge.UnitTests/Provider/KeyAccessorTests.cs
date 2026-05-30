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
        Assert.Throws<ArgumentException>(() => accessor.DecodeKey("1-2"));
    }
}
