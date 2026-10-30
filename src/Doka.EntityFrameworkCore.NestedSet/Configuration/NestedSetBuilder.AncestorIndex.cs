namespace Doka.EntityFrameworkCore.NestedSet;

public sealed partial class NestedSetBuilder<TEntity>
    where TEntity : class
{
    /// <summary>The pending generated property name, or null to retain existing ancestor-index configuration.</summary>
    private string? _ancestorIndexProperty;

    /// <summary>Adds a generated nonleaf flag and an additive access path for selected ancestor queries.</summary>
    /// <param name="nonLeafPropertyName">The generated Boolean property name; a shadow property is created if absent.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The property name is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The completed model cannot generate the flag in the hierarchy table.</exception>
    /// <remarks>
    /// The generated flag is true when Right is greater than Left plus one. This opt-in changes the database
    /// model and requires a migration. The index contains optional scope,
    /// the flag, TreeId, Right, Left, and NodeKey, with repeated properties removed. Existing hierarchy indexes
    /// remain. Generated values follow database writes and are refreshed for tracked nodes; the flag is not
    /// writable hierarchy structure. The database optimizer still chooses the execution plan.
    /// EF property bags use a required Boolean indexer with the framework's natural missing-key sentinel.
    /// Callers do not need to initialize the generated dictionary entry.
    /// New flag properties are declared during model finalization after application table and inheritance
    /// configuration. Map a property explicitly first when referring to it in an application-owned index.
    /// </remarks>
    public NestedSetBuilder<TEntity> HasAncestorIndex(
        string nonLeafPropertyName = "NestedSetIsNonLeaf"
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonLeafPropertyName);
        _ancestorIndexProperty = nonLeafPropertyName;

        return this;
    }

    /// <summary>Applies the pending flag only after the complete configuration callback succeeds.</summary>
    private void CompleteAncestorIndex()
    {
        if (_ancestorIndexProperty is not null)
        {
            _builder.Metadata.SetAnnotation(NestedSetAnnotationNames.AncestorIndexProperty, _ancestorIndexProperty);
        }

        NestedSetAncestorIndex.EnsureProperty((IConventionEntityType)_builder.Metadata);
    }
}
