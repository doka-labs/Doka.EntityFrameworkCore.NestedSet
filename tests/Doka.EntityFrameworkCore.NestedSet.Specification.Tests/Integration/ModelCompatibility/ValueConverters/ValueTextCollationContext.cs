namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses custom converters for nullable value parents.</summary>
internal sealed class ValueTextCollationContext
    : ConvertedTextCollationContext<ValueTextKey, ValueTextKey?>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ValueTextCollationContext(
        DbContextOptions<ValueTextCollationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string Table => "ValueTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<ValueTextKey, ValueTextKey?>> node
    )
    {
        node
            .Property(value => value.Id)
            .HasConversion(value => value.Value, value => new ValueTextKey(value));

        node
            .Property(value => value.ParentId)
            .HasConversion(
                value => value.HasValue
                    ? value.Value.Value
                    : null,
                value => value == null
                    ? null
                    : new ValueTextKey(value));
    }
}
