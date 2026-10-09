using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AdminForge.Tests;

/// <summary>EfSource's query translation, against SQLite rather than an in-memory fake that cannot fail to translate.</summary>
public sealed class EfSourceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private TestDb _db = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Tickets.AddRange(
            new Ticket
            {
                Title = "Printer jam",
                AssigneeId = 1,
                Notes = "tray 2",
            },
            new Ticket { Title = "100% done", Status = TicketStatus.Done },
            new Ticket { Title = "100 percent", AssigneeId = 2 },
            new Ticket { Title = "a_b", Status = TicketStatus.Done },
            new Ticket { Title = "axb", Notes = "PRINTER room" },
            new Ticket { Title = @"c\d" }
        );
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public static TheoryData<string, string?, string?, object?, int[]> Queries =>
        new()
        {
            { "text filter contains, any case", null, "Title", "PRINT", [1] },
            { "% is a literal", null, "Title", "%", [2] },
            { "_ is a literal", null, "Title", "_", [4] },
            { @"\ is a literal", null, "Title", @"\", [6] },
            { "enum filter equals", null, "Status", TicketStatus.Done, [2, 4] },
            { "nullable int filter equals", null, "AssigneeId", 2, [3] },
            { "search spans text columns", "printer", null, null, [1, 5] },
            { "search escapes too", "100%", null, null, [2] },
            { "search and filter both apply", "printer", "Status", TicketStatus.Done, [] },
        };

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task A_query_is_translated_to_SQL(
        string _,
        string? search,
        string? filter,
        object? value,
        int[] ids
    )
    {
        var query = new AdminQuery<Ticket>(
            new(0, 50),
            search,
            filters: filter is null ? null : new Dictionary<string, object?> { [filter] = value }
        );
        var page = await new EfSource<TestDb, Ticket>(_db).ListAsync(query, default);
        Assert.Equal(ids, page.Rows.Select(t => t.Id));
        Assert.Equal(ids.Length, page.Total);
    }

    [Fact]
    public async Task A_page_is_sorted_by_the_column_asked_for_else_by_the_key()
    {
        var source = new EfSource<TestDb, Ticket>(_db);
        var sorted = await source.ListAsync(
            new AdminQuery<Ticket>(new(1, 2), sortBy: "Title", sortDescending: true),
            default
        );
        Assert.Equal(["a_b", "Printer jam"], sorted.Rows.Select(t => t.Title));
        Assert.Equal(6, sorted.Total);

        // Unsorted, a page is still deterministic; a direction without a column changes nothing.
        var unsorted = await source.ListAsync(
            new AdminQuery<Ticket>(new(0, 3), sortDescending: true),
            default
        );
        Assert.Equal([1, 2, 3], unsorted.Rows.Select(t => t.Id));
    }
}
