namespace Doka.EntityFrameworkCore.NestedSet;

public sealed partial class NestedSetBuilder<TEntity>
    where TEntity : class
{
    /// <summary>The pending physical registry table name, or null to retain the existing configuration.</summary>
    private string? _treeRegistryTable;

    /// <summary>Names this hierarchy's typed tree registry table instead of using the generated default.</summary>
    /// <param name="tableName">An unqualified table name; the schema stays that of the hierarchy table.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">
    /// The name is empty, has surrounding whitespace, or contains a separator, quote or control character.
    /// </exception>
    /// <remarks>
    /// The name is a provider-quoted identifier, not SQL. Only the physical name changes: keys, columns, locking,
    /// revisions and tombstones stay as they are. Each hierarchy keeps its own registry, so names must be unique.
    /// Length limits and collisions with other tables are rejected during model finalization. Setting this on an
    /// existing database is a schema change that needs an ordinary migration renaming the table; review the
    /// generated SQL. Without this option the generated <c>DokaNestedSetTrees_*</c> name remains unchanged.
    /// </remarks>
    public NestedSetBuilder<TEntity> HasTreeRegistryTable(
        string tableName
    )
    {
        NestedSetTreeRegistryMetadata.ValidateTableName(tableName);
        _treeRegistryTable = tableName;

        return this;
    }

    /// <summary>Publishes the pending name only after the complete configuration callback succeeds.</summary>
    private void CompleteTreeRegistry()
    {
        if (_treeRegistryTable is not null)
        {
            _builder.Metadata.SetAnnotation(NestedSetAnnotationNames.TreeRegistryTable, _treeRegistryTable);
        }
    }
}
