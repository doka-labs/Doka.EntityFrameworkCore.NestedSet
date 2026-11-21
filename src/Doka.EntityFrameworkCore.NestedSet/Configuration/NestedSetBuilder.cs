namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Maps an entity's persisted nested-set structure.</summary>
/// <typeparam name="TEntity">The mapped entity type.</typeparam>
/// <remarks>Configuration is applied after the callback succeeds. Builder instances are not thread-safe.</remarks>
[PublicAPI]
public sealed partial class NestedSetBuilder<TEntity>
    where TEntity : class
{
    /// <summary>The EF builder that receives configuration after the callback completes.</summary>
    private readonly EntityTypeBuilder<TEntity> _builder;

    /// <summary>The pending CLR property selected for each structural role.</summary>
    private readonly Dictionary<string, (string Name, Type Type)> _properties = new(StringComparer.Ordinal);

    /// <summary>The pending sibling-order criteria, or null to retain previously configured criteria.</summary>
    private List<(string Name, bool Descending, NullSortOrder? NullSortOrder)>? _order;

    /// <summary>The pending policy for explicit placement when sibling order is configured.</summary>
    private NestedSetOrderMode? _orderMode;

    /// <summary>
    /// Whether application-managed concurrency tokens are explicitly preserved by structure-only writes.
    /// </summary>
    private bool _preserveApplicationConcurrencyTokens;

    /// <summary>Creates a pending nested-set configuration for an EF entity.</summary>
    /// <param name="builder">The entity builder that receives the completed mapping.</param>
    internal NestedSetBuilder(
        EntityTypeBuilder<TEntity> builder
    )
    {
        _builder = builder;
    }

    /// <summary>Selects the stable scalar key used by hierarchy operations.</summary>
    /// <typeparam name="TKey">The primary key type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    /// <remarks>
    /// The node key may be a scalar primary key or a scalar alternate key. With a composite EF primary key,
    /// configure an alternate key over the node key, or over scope and node key when scope is configured.
    /// </remarks>
    public NestedSetBuilder<TEntity> HasNodeKey<TKey>(
        Expression<Func<TEntity, TKey>> property
    ) => Map(NestedSetPropertyRoles.NodeKey, property);

    /// <summary>Selects an already mapped scalar node-key property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasNodeKey(
        string propertyName
    ) => Map(NestedSetPropertyRoles.NodeKey, propertyName);

    /// <summary>Selects the stable tree identity stored on every node.</summary>
    /// <typeparam name="TTreeId">The tree identity type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasTreeId<TTreeId>(
        Expression<Func<TEntity, TTreeId>> property
    ) => Map(NestedSetPropertyRoles.TreeId, property);

    /// <summary>Selects an already mapped tree-identity property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasTreeId(
        string propertyName
    ) => Map(NestedSetPropertyRoles.TreeId, propertyName);

    /// <summary>Selects the positive inclusive left and right boundaries.</summary>
    /// <param name="left">The direct left boundary property selector.</param>
    /// <param name="right">The direct right boundary property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">A selector is null.</exception>
    /// <exception cref="ArgumentException">A selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasBounds(
        Expression<Func<TEntity, long>> left,
        Expression<Func<TEntity, long>> right
    )
    {
        var leftProperty = Select(left);
        var rightProperty = Select(right);

        // WHY: Both selectors must be valid before either pending boundary mapping is changed.
        _properties[NestedSetPropertyRoles.Left] = leftProperty;
        _properties[NestedSetPropertyRoles.Right] = rightProperty;

        return this;
    }

    /// <summary>Selects already mapped inclusive left and right boundary properties by name.</summary>
    /// <param name="leftPropertyName">The mapped left boundary property name.</param>
    /// <param name="rightPropertyName">The mapped right boundary property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A name is empty or its property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasBounds(
        string leftPropertyName,
        string rightPropertyName
    )
    {
        var leftProperty = Select(leftPropertyName);
        var rightProperty = Select(rightPropertyName);

        // WHY: Both names must resolve before either pending boundary mapping is changed.
        _properties[NestedSetPropertyRoles.Left] = leftProperty;
        _properties[NestedSetPropertyRoles.Right] = rightProperty;

        return this;
    }

    /// <summary>Selects the optional application scope that partitions tree identities and node keys.</summary>
    /// <typeparam name="TScope">The scope key type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasScope<TScope>(
        Expression<Func<TEntity, TScope>> property
    ) => Map(NestedSetPropertyRoles.Scope, property);

    /// <summary>Selects an already mapped optional scope property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasScope(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Scope, propertyName);

    /// <summary>Selects the nullable parent key; null denotes a root.</summary>
    /// <typeparam name="TParent">The nullable primary key type.</typeparam>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasParent<TParent>(
        Expression<Func<TEntity, TParent>> property
    ) => Map(NestedSetPropertyRoles.Parent, property);

    /// <summary>Selects an already mapped nullable parent-node-key property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasParent(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Parent, propertyName);

    /// <summary>Selects the persisted depth; roots have depth zero.</summary>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasDepth(
        Expression<Func<TEntity, int>> property
    ) => Map(NestedSetPropertyRoles.Depth, property);

    /// <summary>Selects an already mapped required Int32 depth property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasDepth(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Depth, propertyName);

    /// <summary>Selects the persisted, dense, zero-based position among siblings in the same tree.</summary>
    /// <param name="property">A direct, non-null property selector.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">The selector is null.</exception>
    /// <exception cref="ArgumentException">The selector does not select a direct property.</exception>
    public NestedSetBuilder<TEntity> HasPosition(
        Expression<Func<TEntity, long>> property
    ) => Map(NestedSetPropertyRoles.Position, property);

    /// <summary>Selects an already mapped required Int64 sibling-position property by name.</summary>
    /// <param name="propertyName">The mapped CLR, field-only, indexer, or shadow property name.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The name is empty or the property is not mapped.</exception>
    public NestedSetBuilder<TEntity> HasPosition(
        string propertyName
    ) => Map(NestedSetPropertyRoles.Position, propertyName);

    /// <summary>Declares that structure-only writes preserve application-managed concurrency-token values.</summary>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Normal EF payload saves continue to use the token for optimistic concurrency. Nested-set coordinate,
    /// position, parent, and TreeId updates do not assign a new token value. Store-generated concurrency tokens
    /// are refreshed independently and do not require this policy.
    /// </remarks>
    public NestedSetBuilder<TEntity> PreserveApplicationConcurrencyTokens()
    {
        _preserveApplicationConcurrencyTokens = true;

        return this;
    }
}
