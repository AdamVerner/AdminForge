using AdminForge.UI.Blazor;

namespace AdminForge.UnitTests.UI;

/// <summary>
/// The related-link buttons emit URLs of the shape
/// <c>/admin/entities/Todo?filter:TodoListId=42</c>. The list page seeds its filter
/// bar from those entries; this test pins the parsing contract.
/// </summary>
public class FilterUrlParserTests
{
    [Fact]
    public void Parse_Picks_Up_Filter_Prefixed_Entries()
    {
        var result = FilterUrlParser.Parse(
            "/admin/entities/Todo?filter:TodoListId=42&filter:Status=Open&other=ignored"
        );
        Assert.Equal(2, result.Count);
        Assert.Equal("42", result["TodoListId"]);
        Assert.Equal("Open", result["Status"]);
        Assert.False(result.ContainsKey("other"));
    }

    [Fact]
    public void Parse_Returns_Empty_When_No_Query_String()
    {
        var result = FilterUrlParser.Parse("/admin/entities/Todo");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_Ignores_Empty_Filter_Key()
    {
        var result = FilterUrlParser.Parse("/admin/entities/Todo?filter:=oops");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_Handles_Url_Encoded_Values()
    {
        var result = FilterUrlParser.Parse("/admin/entities/Todo?filter:Title=hello%20world");
        Assert.Equal("hello world", result["Title"]);
    }
}
