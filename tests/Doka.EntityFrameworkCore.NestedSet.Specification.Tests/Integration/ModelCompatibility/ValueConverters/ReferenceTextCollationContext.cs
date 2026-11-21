namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses custom converters for nullable reference parents.</summary>
internal sealed class ReferenceTextCollationContext
    : ConvertedTextCollationContext<ReferenceTextKey, ReferenceTextKey?>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ReferenceTextCollationContext(
        DbContextOptions<ReferenceTextCollationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string Table => "ReferenceTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<ReferenceTextKey, ReferenceTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasConversion(value => value.Value, value => new ReferenceTextKey(value));

        node
            .Property(value => value.ParentId)
            .HasConversion(value => value!.Value, value => new ReferenceTextKey(value));
    }
}
