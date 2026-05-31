using AdminForge.Core.Configuration;
using AdminForge.Core.Metadata;
using AdminForge.DataAccess.EfCore;
using AdminForge.UnitTests.Fixtures;
using TodoApp.Entities;

namespace AdminForge.UnitTests.Configuration;

public class AdminForgeBuilderTests
{
    private static IReadOnlyList<EntityMeta> Scan()
    {
        using var ctx = TodoContextFactory.CreateInMemory();
        return new EfCoreReflectionScanner().Scan(ctx);
    }

    [Fact]
    public void Build_Includes_Registered_Entities_In_Order()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>().AddTable<Todo>();

        var options = builder.Build();

        Assert.Collection(
            options.Entities,
            e => Assert.Equal("User", e.Name),
            e => Assert.Equal("Todo", e.Name)
        );
    }

    [Fact]
    public void AddTable_Throws_On_Unknown_Type()
    {
        var builder = new AdminForgeBuilder([]); // empty scan
        Assert.Throws<InvalidOperationException>(() => builder.AddTable<User>());
    }

    [Fact]
    public void AddTable_Rejects_Duplicate_Registration()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>();
        Assert.Throws<InvalidOperationException>(() => builder.AddTable<User>());
    }

    [Fact]
    public void Nav_Overrides_Apply_To_Underlying_Meta()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e => e.Nav(n => n.Label("Tasks").Group("Work").Order(1)));

        var meta = builder.Build().Entities.Single();
        Assert.Equal("Tasks", meta.Nav.Label);
        Assert.Equal("Work", meta.Nav.Group);
        Assert.Equal(1, meta.Nav.Order);
    }

    [Fact]
    public void Column_Overrides_Apply_To_Targeted_Column()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e =>
            e.Column(t => t.Title, c => c.Label("Headline").Description("Short summary"))
        );

        var title = builder
            .Build()
            .Entities.Single()
            .Columns.Single(c => c.PropertyName == "Title");
        Assert.Equal("Headline", title.Label);
        Assert.Equal("Short summary", title.Description);
    }

    [Fact]
    public void DisplayMember_Override_Resolves_To_Selected_Property()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>(e => e.DisplayMember(u => u.Email));

        var meta = builder.Build().Entities.Single();
        Assert.NotNull(meta.DisplayLabel);
        var label = meta.DisplayLabel!(new User { Email = "x@y.z", DisplayName = "X" });
        Assert.Equal("x@y.z", label);
    }

    [Fact]
    public void Default_DisplayLabel_Uses_Heuristic()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<User>();
        var meta = builder.Build().Entities.Single();

        // User has DisplayName which sits ahead of Email/Name in the preferred list... actually
        // the preference order is Name → Title → Label → DisplayName → Email. User has none
        // named "Name"/"Title"/"Label", so DisplayName wins.
        Assert.Equal(
            "Alice",
            meta.DisplayLabel!(new User { DisplayName = "Alice", Email = "alice@example.com" })
        );
    }

    [Fact]
    public void PolicyNames_Format_Is_Stable()
    {
        Assert.Equal("AdminForge:Todo:Read", PolicyNames.For("Todo", AdminAction.Read));
        Assert.Equal("AdminForge:User:Delete", PolicyNames.For("User", AdminAction.Delete));
    }

    [Fact]
    public void WithLivePolling_Stores_Interval_On_EntityMeta()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e => e.WithLivePolling(TimeSpan.FromSeconds(7)));

        var meta = builder.Build().Entities.Single();
        Assert.Equal(TimeSpan.FromSeconds(7), meta.LivePollingInterval);
    }

    [Fact]
    public void WithLivePolling_Rejects_Non_Positive_Interval()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddTable<Todo>(e => e.WithLivePolling(TimeSpan.Zero))
        );
    }

    [Fact]
    public void WithTheme_Defaults_Are_All_Null()
    {
        var options = new AdminForgeBuilder(Scan()).Build();
        Assert.NotNull(options.Theme);
        Assert.Null(options.Theme.LogoUrl);
        Assert.Null(options.Theme.LogoAlt);
        Assert.Null(options.Theme.PrimaryColor);
        Assert.Null(options.Theme.SecondaryColor);
    }

    [Fact]
    public void WithTheme_Configures_All_Properties()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.WithTheme(t =>
        {
            t.LogoUrl = "/logo.png";
            t.LogoAlt = "MyApp";
            t.PrimaryColor = "#00897b";
            t.SecondaryColor = "#ff8a65";
        });
        var theme = builder.Build().Theme;
        Assert.Equal("/logo.png", theme.LogoUrl);
        Assert.Equal("MyApp", theme.LogoAlt);
        Assert.Equal("#00897b", theme.PrimaryColor);
        Assert.Equal("#ff8a65", theme.SecondaryColor);
    }

    [Fact]
    public void WithTheme_Rejects_Null_Configure()
    {
        var builder = new AdminForgeBuilder(Scan());
        Assert.Throws<ArgumentNullException>(() => builder.WithTheme(null!));
    }

    [Fact]
    public void Validator_Captures_Predicate_And_Message()
    {
        var builder = new AdminForgeBuilder(Scan());
        builder.AddTable<Todo>(e =>
            e.Column(
                t => t.Title,
                c => c.Validate(value => value is string s && s.Length > 0, "Title required")
            )
        );

        var title = builder
            .Build()
            .Entities.Single()
            .Columns.Single(c => c.PropertyName == "Title");
        var validator = Assert.Single(title.Validators);
        Assert.Equal("Title required", validator.Validate(""));
        Assert.Null(validator.Validate("ok"));
    }
}
