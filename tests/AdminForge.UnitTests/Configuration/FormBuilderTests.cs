using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;

namespace AdminForge.UnitTests.Configuration;

public class FormBuilderTests
{
    [Fact]
    public void AddForm_Registers_Title_Description_Fields_In_Order()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "send-notification",
            f =>
                f.WithTitle("Send Notification")
                    .WithDescription("All fields")
                    .AddField(x => x.Text("Title").Required())
                    .AddField(x => x.Bool("Urgent"))
                    .AddField(x => x.Number("Priority").Min(0).Max(5))
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var form = Assert.Single(builder.Build().Forms);
        Assert.Equal("send-notification", form.RouteName);
        Assert.Equal("Send Notification", form.Title);
        Assert.Equal("All fields", form.Description);
        Assert.Collection(
            form.Fields,
            x => Assert.Equal(FieldKind.Text, x.Kind),
            x => Assert.Equal(FieldKind.Bool, x.Kind),
            x => Assert.Equal(FieldKind.Number, x.Kind)
        );
        Assert.True(form.Fields[0].Required);
    }

    [Fact]
    public void AddForm_Throws_On_Duplicate_RouteName()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "a",
            f => f.AddField(x => x.Text("X")).OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddForm(
                "A",
                f => f.AddField(x => x.Text("X")).OnSubmit((sp, sub, ctx) => Task.CompletedTask)
            )
        );
    }

    [Fact]
    public void AddForm_Throws_When_OnSubmit_Missing()
    {
        var builder = new AdminForgeBuilder();
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddForm("no-handler", f => f.AddField(x => x.Text("X")))
        );
    }

    [Fact]
    public void Field_Validator_Is_Captured()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "f",
            f =>
                f.AddField(x =>
                        x.Text("Body").Validate(v => v is string s && s.Length >= 3, "Min 3 chars")
                    )
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var field = builder.Build().Forms.Single().Fields.Single();
        var validator = Assert.Single(field.Validators);
        Assert.Equal("Min 3 chars", validator.Validate("x"));
        Assert.Null(validator.Validate("hello"));
    }

    [Fact]
    public void TextFieldOptions_Capture_Multiline_And_MaxLength()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "f",
            f =>
                f.AddField(x => x.Text("Body").Multiline().MaxLength(50))
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var opts = Assert.IsType<TextFieldOptions>(
            builder.Build().Forms.Single().Fields.Single().Options
        );
        Assert.True(opts.Multiline);
        Assert.Equal(50, opts.MaxLength);
    }

    [Fact]
    public void NumberFieldOptions_Capture_Min_Max()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "f",
            f =>
                f.AddField(x => x.Number("N").Min(2).Max(7))
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var opts = Assert.IsType<NumberFieldOptions>(
            builder.Build().Forms.Single().Fields.Single().Options
        );
        Assert.Equal(2, opts.Min);
        Assert.Equal(7, opts.Max);
    }

    [Fact]
    public void FloatFieldOptions_Capture_Min_Max()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "f",
            f =>
                f.AddField(x => x.Float("F").Min(0.5).Max(2.5))
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var opts = Assert.IsType<FloatFieldOptions>(
            builder.Build().Forms.Single().Fields.Single().Options
        );
        Assert.Equal(0.5, opts.Min);
        Assert.Equal(2.5, opts.Max);
    }

    [Fact]
    public void FileUploadFieldOptions_Capture_MaxSize_And_Extensions()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "f",
            f =>
                f.AddField(x =>
                        x.FileUpload("F").MaxSizeBytes(1024).AcceptedExtensions(".PDF", "png")
                    )
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var opts = Assert.IsType<FileUploadFieldOptions>(
            builder.Build().Forms.Single().Fields.Single().Options
        );
        Assert.Equal(1024, opts.MaxSizeBytes);
        // AcceptedExtensions normalises to lowercase + dot prefix.
        Assert.NotNull(opts.AcceptedExtensions);
        Assert.Equal(new[] { ".pdf", ".png" }, opts.AcceptedExtensions!);
    }

    [Fact]
    public void All_Eight_FieldKinds_Are_Reachable()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "all",
            f =>
                f.AddField(x => x.Text("Text"))
                    .AddField(x => x.Number("N"))
                    .AddField(x => x.Float("F"))
                    .AddField(x => x.Bool("B"))
                    .AddField(x => x.Date("D"))
                    .AddField(x => x.DateTime("Dt"))
                    .AddField(x => x.Markdown("Md"))
                    .AddField(x => x.FileUpload("File"))
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var kinds = builder.Build().Forms.Single().Fields.Select(x => x.Kind).ToArray();
        Assert.Equal(
            new[]
            {
                FieldKind.Text,
                FieldKind.Number,
                FieldKind.Float,
                FieldKind.Bool,
                FieldKind.Date,
                FieldKind.DateTime,
                FieldKind.Markdown,
                FieldKind.FileUpload,
            },
            kinds
        );
    }

    [Fact]
    public void FieldBuilder_Throws_When_Two_Kinds_Called()
    {
        var builder = new AdminForgeBuilder();
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddForm(
                "f",
                f =>
                    f.AddField(x =>
                        {
                            x.Text("A");
                            x.Number("B");
                        })
                        .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
            )
        );
    }

    [Fact]
    public void Nav_Group_And_Label_Are_Captured()
    {
        var builder = new AdminForgeBuilder();
        builder.AddForm(
            "send-notification",
            f =>
                f.WithTitle("Send Notification")
                    .Nav(n => n.Group("Tools").Order(1).Label("Notify"))
                    .AddField(x => x.Text("Title").Required())
                    .OnSubmit((sp, sub, ctx) => Task.CompletedTask)
        );

        var form = builder.Build().Forms.Single();
        Assert.Equal("Tools", form.Nav.Group);
        Assert.Equal(1, form.Nav.Order);
        Assert.Equal("Notify", form.Nav.Label);
    }
}

public class FormSubmissionTests
{
    [Fact]
    public void Get_Returns_Default_For_Missing_Key()
    {
        var sub = new FormSubmission(new Dictionary<string, object?>());
        Assert.Null(sub.Get<string>("nope"));
        Assert.Equal(0, sub.Get<int>("nope"));
        Assert.Null(sub.Get<int?>("nope"));
    }

    [Fact]
    public void Get_Roundtrips_Direct_And_Coerced_Types()
    {
        var values = new Dictionary<string, object?>
        {
            ["A"] = "hello",
            ["B"] = 42,
            ["C"] = "3.14",
            ["D"] = true,
            ["E"] = DateTime.UtcNow,
        };
        var sub = new FormSubmission(values);
        Assert.Equal("hello", sub.Get<string>("A"));
        Assert.Equal(42, sub.Get<int>("B"));
        Assert.Equal(3.14, sub.Get<double>("C"));
        Assert.True(sub.Get<bool>("D"));
        Assert.True(sub.TryGet<DateTime>("E", out var dt));
        Assert.Equal(values["E"], dt);
    }

    [Fact]
    public void TryGet_Returns_False_On_Missing()
    {
        var sub = new FormSubmission(new Dictionary<string, object?> { ["A"] = null });
        Assert.False(sub.TryGet<string>("missing", out _));
        Assert.False(sub.TryGet<string>("A", out _));
    }

    [Fact]
    public void Indexer_Returns_Stored_Value()
    {
        var sub = new FormSubmission(new Dictionary<string, object?> { ["A"] = "x" });
        Assert.Equal("x", sub["A"]);
        Assert.Null(sub["B"]);
    }

    [Fact]
    public void Files_Default_Empty_When_Not_Provided()
    {
        var sub = new FormSubmission(new Dictionary<string, object?>());
        Assert.Empty(sub.Files);
    }

    [Fact]
    public void FormFileUpload_OpenReadStream_Returns_Fresh_Stream_Each_Call()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var file = new FormFileUpload("a.bin", "application/octet-stream", bytes);
        Assert.Equal(4, file.Length);
        using var s1 = file.OpenReadStream();
        using var s2 = file.OpenReadStream();
        Assert.Equal(0, s1.Position);
        Assert.Equal(0, s2.Position);
        s1.ReadByte();
        Assert.Equal(0, s2.Position); // independent streams
    }
}
