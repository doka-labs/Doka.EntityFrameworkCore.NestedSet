namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses the same nullable model type for the required key and optional parent.</summary>
internal sealed class NullableEnumTextCollationContext
    : ConvertedTextCollationContext<EnumTextKey?, EnumTextKey?>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public NullableEnumTextCollationContext(
        DbContextOptions<NullableEnumTextCollationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string Table => "NullableEnumTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<EnumTextKey?, EnumTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasConversion<string>();

        node
            .Property(value => value.ParentId)
            .HasConversion<string>();
    }
}
