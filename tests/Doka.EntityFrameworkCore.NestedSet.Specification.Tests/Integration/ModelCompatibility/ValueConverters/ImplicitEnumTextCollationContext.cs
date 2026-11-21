namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Lets the relational text store type select the effective enum converter.</summary>
internal sealed class ImplicitEnumTextCollationContext
    : ConvertedTextCollationContext<EnumTextKey, EnumTextKey?>
{
    /// <summary>Uses the configured model and database connection.</summary>
    /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
    public ImplicitEnumTextCollationContext(
        DbContextOptions<ImplicitEnumTextCollationContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string Table => "ImplicitEnumTextCollation";

    /// <inheritdoc />
    protected override void ConvertKeys(
        EntityTypeBuilder<ConvertedTextCollationNode<EnumTextKey, EnumTextKey?>> node
    )
    {
        var storeType = Database.IsSqlite()
            ? "TEXT"
            : Database.IsNpgsql()
                ? "character varying(64)"
                : "varchar(64)";

        node
            .Property(value => value.Id)
            .HasColumnType(storeType);

        node
            .Property(value => value.ParentId)
            .HasColumnType(storeType);
    }
}
