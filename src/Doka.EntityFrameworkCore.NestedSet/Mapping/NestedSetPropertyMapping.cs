namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Retains validated scalar metadata and its physical column without capturing provider services.</summary>
internal sealed class NestedSetPropertyMapping
{
    /// <summary>Resolves the physical column and provider parameter mapping once for this EF model.</summary>
    /// <param name="property">The validated structural or configured ordering property.</param>
    /// <param name="store">The hierarchy's single table.</param>
    internal NestedSetPropertyMapping(
        IProperty property,
        StoreObjectIdentifier store
    )
    {
        Property = property;
        ColumnName = property.GetColumnName(store)
            ?? throw new InvalidOperationException("The nested-set property must map to the hierarchy table.");

        TypeMapping = property.GetRelationalTypeMapping();
    }

    /// <summary>Gets the finalized EF property, including CLR type, comparers, and save behavior.</summary>
    internal IProperty Property { get; }

    /// <summary>Gets the physical column name before provider-specific identifier delimiting.</summary>
    internal string ColumnName { get; }

    /// <summary>Gets the exact relational mapping used when constructing command parameters.</summary>
    internal RelationalTypeMapping TypeMapping { get; }
}
