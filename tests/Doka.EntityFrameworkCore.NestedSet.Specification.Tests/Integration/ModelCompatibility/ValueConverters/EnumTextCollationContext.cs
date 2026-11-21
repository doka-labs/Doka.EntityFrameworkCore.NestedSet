namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses provider-type configuration rather than an explicitly supplied converter.</summary>
internal sealed class EnumTextCollationContext
    : ConvertedTextCollationContext<EnumTextKey, EnumTextKey?>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public EnumTextCollationContext(
        DbContextOptions<EnumTextCollationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string Table => "EnumTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>> node
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
