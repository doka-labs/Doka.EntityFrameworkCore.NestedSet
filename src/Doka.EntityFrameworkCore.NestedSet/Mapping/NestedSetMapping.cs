namespace Doka.EntityFrameworkCore.NestedSet.Mapping;

/// <summary>Validates and retains the model metadata used by structural operations.</summary>
/// <typeparam name="TEntity">The mapped nested-set entity.</typeparam>
/// <typeparam name="TKey">The scalar NodeKey CLR type.</typeparam>
/// <typeparam name="TScope">The Scope CLR type, or the internal scopeless marker.</typeparam>
internal sealed class NestedSetMapping<TEntity, TKey, TScope>
    where TEntity : class
    where TKey : notnull
    where TScope : notnull
{
    // WHY: The cache follows EF model lifetime and never owns a context or a forest scope.
    private static readonly ConditionalWeakTable<IEntityType, NestedSetMapping<TEntity, TKey, TScope>> s_entities =
        new();

    private readonly Dictionary<string, LambdaExpression> _selectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NestedSetPropertyMapping> _properties = new(StringComparer.Ordinal);

    /// <summary>Returns immutable metadata for one exact ordinary or named shared entity type.</summary>
    /// <param name="context">The context whose finalized model contains the mapping.</param>
    /// <param name="entityType">The exact hierarchy entity type.</param>
    /// <returns>The validated mapping, cached for the entity-type metadata lifetime.</returns>
    internal static NestedSetMapping<TEntity, TKey, TScope> For(
        DbContext context,
        IEntityType entityType
    ) => s_entities.GetValue(entityType, _ => new NestedSetMapping<TEntity, TKey, TScope>(context, entityType));

    /// <summary>Resolves a supported mapping before any nested-set query or mutation can run.</summary>
    /// <param name="context">The context whose finalized model contains the hierarchy and its tree registry.</param>
    /// <param name="entityType">The exact ordinary or named shared hierarchy entity type.</param>
    /// <exception cref="InvalidOperationException">
    ///     The mapping is incomplete or unsupported by the update protocol.
    /// </exception>
    private NestedSetMapping(
        DbContext context,
        IEntityType entityType
    )
    {
        EntityType = entityType;

        var modelMapping = NestedSetModelMapping.For(context.Model);
        var descriptor = modelMapping.Descriptor(EntityType);
        ValidateTable();
        Store = NestedSetStoreObject.Resolve(EntityType, descriptor);

        Key = descriptor.NodeKey.Name;
        Left = descriptor.Left.Name;
        Right = descriptor.Right.Name;
        Depth = descriptor.Depth.Name;
        Scope = descriptor.Scope?.Name;
        TreeId = descriptor.TreeId.Name;
        Parent = descriptor.Parent.Name;
        Position = descriptor.Position.Name;
        var names = new List<string>
        {
            Key,
            Left,
            Right,
            Depth,
            TreeId,
            Parent,
            Position,
        };

        if (Scope is not null)
        {
            names.Add(Scope);
        }

        foreach (var name in names)
        {
            var property = EntityType.FindProperty(name)
                ?? throw new InvalidOperationException($"Nested-set property '{name}' is not mapped.");

            _properties.Add(name, new NestedSetPropertyMapping(property, Store));
        }

        KeyProperty = RequiredProperty(Key);
        ScopeProperty = Scope is null ? null : RequiredProperty(Scope);
        TreeIdProperty = RequiredProperty(TreeId);
        ParentProperty = RequiredProperty(Parent);
        LeftProperty = RequiredProperty(Left);
        RightProperty = RequiredProperty(Right);
        DepthProperty = RequiredProperty(Depth);
        PositionProperty = RequiredProperty(Position);
        var keyProperty = KeyProperty;
        var scopeProperty = ScopeProperty;
        var parentProperty = ParentProperty;

        if (keyProperty.ClrType != typeof(TKey))
        {
            throw new InvalidOperationException("The nested-set NodeKey must match TKey.");
        }

        if (scopeProperty is null)
        {
            if (typeof(TScope) != typeof(NestedSetNoScope))
            {
                throw new InvalidOperationException(
                    "A scopeless nested set requires the internal scopeless operation binding.");
            }
        }
        else if (scopeProperty.ClrType != typeof(TScope)
                 || scopeProperty.IsNullable)
        {
            throw new InvalidOperationException("The nested-set scope must be required and match TScope.");
        }

        // WHY: Deletion explicitly promotes or detaches children; cascades would bypass that ordered protocol.
        if (EntityType
            .GetForeignKeys()
            .Any(foreignKey => foreignKey.PrincipalEntityType == EntityType
                && foreignKey.Properties.Contains(parentProperty)
                && foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade))
        {
            throw new InvalidOperationException("Nested-set self references must not cascade deletes.");
        }

        // WHY: Set updates temporarily overlap positions or stage negative bounds before restoring a valid forest.
        if (EntityType
                .GetIndexes()
                .Any(index => index.IsUnique && index.Properties.Any(IsMutableStructure))
            || EntityType
                .GetKeys()
                .Any(candidate => candidate.Properties.Any(IsMutableStructure)))
        {
            throw new InvalidOperationException("Unique nested-set structural indexes and keys are unsupported.");
        }

        KeyComparer = new NestedSetKeyComparer<TKey>(keyProperty.GetKeyValueComparer());
        ScopeComparer = new NestedSetProviderComparer<TScope>(scopeProperty);

        // WHY: Only unchanged native key equality can safely correlate database identities in a CLR lookup.
        // Provider collection transport is a separate capability and cannot be inferred from this proof.
        HasNativeKeyEquality = HasNativeEquality(keyProperty);
        HasNativeScopeEquality = scopeProperty is not null && HasNativeEquality(scopeProperty);
        HasUniqueNodeKey = EntityType
            .GetKeys()
            .Any(key => key.Properties.Count == 1 && key.Properties[0] == keyProperty);

        foreach (var name in names)
        {
            var parameter = Expression.Parameter(typeof(TEntity), "node");
            var access = NestedSetExpressions.Property(
                parameter,
                name,
                RequiredProperty(name).ClrType);

            _selectors.Add(name, Expression.Lambda(access, parameter));
        }

        Projection = NestedSetNode<TKey>.CreateProjection<TEntity>(
            keyProperty,
            parentProperty,
            Left,
            Right,
            Depth,
            Position);

        Order = modelMapping.Ordering(EntityType);

        NestedSetProviderCapabilities
            .Resolve(context)
            .ValidateOrdering(Order);

        if (Order is not null)
        {
            // WHY: Native predecessor SQL reads configured payload columns as well as structural columns.
            // Resolve both sets before publishing this immutable model cache, including converted sort fields.
            foreach (var property in Order.Properties)
            {
                if (!_properties.ContainsKey(property.Name))
                {
                    _properties.Add(property.Name, new NestedSetPropertyMapping(property, Store));
                }
            }
        }

        // WHY: Key identity is defined by provider storage, including converters from non-string model types.
        KeyCollation = NestedSetCollations.Resolve(context, keyProperty, EntityType);

        if (KeyCollation is null
            && NestedSetCollations.Resolve(context, parentProperty, EntityType) is not null)
        {
            throw new InvalidOperationException(
                "Configure the string-backed key collation when the parent property has an explicit collation.");
        }
    }

    /// <summary>Gets structural key equality, including content equality for binary keys.</summary>
    internal IEqualityComparer<TKey> KeyComparer { get; }

    /// <summary>Gets equality for scope bookkeeping in the mapped provider representation.</summary>
    internal IEqualityComparer<TScope> ScopeComparer { get; }

    /// <summary>
    /// Gets whether unconverted integral or Guid identity uses the provider's unchanged key comparer.
    /// </summary>
    internal bool HasNativeKeyEquality { get; }

    /// <summary>
    /// Gets whether unconverted integral or Guid scope values use the provider's unchanged comparer.
    /// </summary>
    internal bool HasNativeScopeEquality { get; }

    /// <summary>Gets whether NodeKey alone is an EF key, so a tracked row needs no scope correlation.</summary>
    internal bool HasUniqueNodeKey { get; }

    /// <summary>Gets the structural-only database projection shared by lookups and inspection.</summary>
    internal Expression<Func<TEntity, NestedSetNode<TKey>>> Projection { get; }

    /// <summary>Gets configured sibling ordering, or null for explicitly maintained sibling positions.</summary>
    internal NestedSetOrdering? Order { get; }

    /// <summary>Gets the immutable physical table identity shared by structural SQL builders.</summary>
    internal StoreObjectIdentifier Store { get; }

    /// <summary>Gets the validated primary key metadata.</summary>
    internal IProperty KeyProperty { get; }

    /// <summary>Gets the validated forest scope metadata.</summary>
    internal IProperty? ScopeProperty { get; }

    /// <summary>Gets the validated stable tree identity metadata.</summary>
    internal IProperty TreeIdProperty { get; }

    /// <summary>Gets the validated nullable parent key metadata.</summary>
    internal IProperty ParentProperty { get; }

    /// <summary>Gets the validated left boundary metadata.</summary>
    internal IProperty LeftProperty { get; }

    /// <summary>Gets the validated right boundary metadata.</summary>
    internal IProperty RightProperty { get; }

    /// <summary>Gets the validated depth metadata.</summary>
    internal IProperty DepthProperty { get; }

    /// <summary>Gets the validated sibling position metadata.</summary>
    internal IProperty PositionProperty { get; }

    /// <summary>
    /// Returns cached column and parameter metadata for a structural or configured ordering property.
    /// </summary>
    internal NestedSetPropertyMapping PropertyMapping(
        string? name
    ) => _properties[name ?? throw new InvalidOperationException("The requested nested-set property is not configured.")];

    /// <summary>Returns the cached selector for a mapped property's exact CLR type.</summary>
    /// <typeparam name="TValue">The mapped property type, including nullable parent keys.</typeparam>
    /// <param name="name">The property name of one structural role.</param>
    /// <returns>An immutable selector that contains no runtime scope or key values.</returns>
    internal Expression<Func<TEntity, TValue>> Property<TValue>(
        string name
    ) => (Expression<Func<TEntity, TValue>>)_selectors[name];

    /// <summary>Gets the entity's single primary key property name.</summary>
    internal string Key { get; }

    /// <summary>Gets the effective physical key collation, or null when the database default is unknown.</summary>
    internal string? KeyCollation { get; }

    /// <summary>Gets the required Int64 left-boundary property name.</summary>
    internal string Left { get; }

    /// <summary>Gets the required Int64 right-boundary property name.</summary>
    internal string Right { get; }

    /// <summary>Gets the required Int32 depth property name.</summary>
    internal string Depth { get; }

    /// <summary>Gets the non-null property name that isolates forests within the table.</summary>
    internal string? Scope { get; }

    /// <summary>Gets the non-null property name identifying the containing tree.</summary>
    internal string TreeId { get; }

    /// <summary>Gets the nullable parent key property name.</summary>
    internal string Parent { get; }

    /// <summary>Gets the required Int64 sibling-position property name.</summary>
    internal string Position { get; }

    /// <summary>Gets the finalized entity metadata used by expression builders and CLR accessors.</summary>
    internal IEntityType EntityType { get; }

    /// <summary>Rejects entity shapes whose table or query semantics could hide rows from structural updates.</summary>
    /// <exception cref="InvalidOperationException">The entity does not map to one independent table.</exception>
    private void ValidateTable()
    {
        if (EntityType.IsOwned()
            || EntityType.IsMappedToJson()
            || EntityType.GetViewName() is not null)
        {
            throw new InvalidOperationException(
                "Nested sets require a directly queryable table entity with an independent relational identity.");
        }

        if (EntityType.GetMappingStrategy() == RelationalAnnotationNames.TpcMappingStrategy
            && EntityType
                .GetDerivedTypes()
                .Any())
        {
            throw new InvalidOperationException(
                "A polymorphic TPC hierarchy spans multiple concrete tables. Configure each concrete node type "
                + "as an independent nested-set hierarchy.");
        }
    }

    /// <summary>Resolves a configured property from the finalized entity metadata.</summary>
    /// <param name="name">The required property name.</param>
    /// <returns>The mapped property's metadata.</returns>
    /// <exception cref="InvalidOperationException">No mapped property has the requested name.</exception>
    private IProperty RequiredProperty(
        string name
    ) => _properties[name].Property;

    /// <summary>Checks whether CLR equality of a property's values matches the database comparison exactly.</summary>
    /// <param name="property">The mapped key or scope property.</param>
    /// <returns>Whether an unconverted integral or Guid value uses the provider's unchanged key comparer.</returns>
    private static bool HasNativeEquality(
        IProperty property
    )
    {
        var type = property.ClrType;
        var native = type == typeof(int)
            || type == typeof(long)
            || type == typeof(short)
            || type == typeof(byte)
            || type == typeof(uint)
            || type == typeof(ulong)
            || type == typeof(ushort)
            || type == typeof(sbyte)
            || type == typeof(Guid);

        return native
            && property.GetTypeMapping()
                .Converter is null
            && ReferenceEquals(
                property.GetKeyValueComparer(),
                property.GetTypeMapping().KeyComparer);
    }

    /// <summary>Identifies properties whose intermediate values cannot participate in unique indexes or keys.</summary>
    /// <param name="property">The indexed or keyed property to inspect.</param>
    /// <returns>Whether a structural operation can update this property.</returns>
    private bool IsMutableStructure(
        IProperty property
    ) => property.Name == Left
        || property.Name == Right
        || property.Name == Depth
        || property.Name == TreeId
        || property.Name == Parent
        || property.Name == Position;
}
