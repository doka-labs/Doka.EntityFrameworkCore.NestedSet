namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Stores only persisted hierarchy structure, without materializing a domain entity.</summary>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <param name="Key">The node identity.</param>
/// <param name="Left">The inclusive left boundary.</param>
/// <param name="Right">The inclusive right boundary.</param>
/// <param name="Parent">The nullable stored parent key.</param>
/// <param name="Depth">The number of ancestors.</param>
/// <param name="Position">The zero-based sibling position.</param>
internal readonly record struct NestedSetNode<TKey>(
    TKey Key,
    long Left,
    long Right,
    NestedSetParent<TKey> Parent,
    int Depth,
    long Position
)
    where TKey : notnull
{
    /// <summary>Projects nullable parent keys without boxing a value-type key for every node.</summary>
    /// <typeparam name="TEntity">The mapped hierarchy entity.</typeparam>
    /// <param name="key">The mapped primary key.</param>
    /// <param name="parent">The mapped nullable parent property.</param>
    /// <param name="left">The mapped left-boundary name.</param>
    /// <param name="right">The mapped right-boundary name.</param>
    /// <param name="depth">The mapped depth name.</param>
    /// <param name="position">The mapped sibling-position name.</param>
    /// <returns>A structural projection with typed optional parent storage.</returns>
    internal static Expression<Func<TEntity, NestedSetNode<TKey>>> CreateProjection<TEntity>(
        IProperty key,
        IProperty parent,
        string left,
        string right,
        string depth,
        string position
    )
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "node");
        var optionalParent = NestedSetParent<TKey>.Property(parameter, parent);

        var body = Expression.New(
            typeof(NestedSetNode<TKey>).GetConstructors()[0],
            NestedSetExpressions.Property(parameter, key.Name, typeof(TKey)),
            NestedSetExpressions.Property(parameter, left, typeof(long)),
            NestedSetExpressions.Property(parameter, right, typeof(long)),
            optionalParent,
            NestedSetExpressions.Property(parameter, depth, typeof(int)),
            NestedSetExpressions.Property(parameter, position, typeof(long)));

        return Expression.Lambda<Func<TEntity, NestedSetNode<TKey>>>(body, parameter);
    }
}

/// <summary>Keeps optional value-type keys inline in each structural snapshot.</summary>
/// <typeparam name="TKey">The non-null mapped primary key type.</typeparam>
/// <param name="HasValue">Whether the node references a parent.</param>
/// <param name="Value">The parent key when present; otherwise the unused default value.</param>
internal readonly record struct NestedSetParent<TKey>(
    bool HasValue,
    TKey Value
)
    where TKey : notnull
{
    private static readonly Func<PropertyValues, IProperty, NestedSetParent<TKey>> s_reader = CreateReader();

    /// <summary>Adapts optional keys only for EF metadata assignment or native database parameter binding.</summary>
    internal object? BoxedValue => HasValue
        ? Value
        : null;

    /// <summary>Reads one optional parent through a reused live wrapper and its exact nullable CLR shape.</summary>
    /// <param name="values">The current values shared with the entry's other structural reads.</param>
    /// <param name="property">The mapped nullable parent property.</param>
    /// <returns>A present typed parent, including default-valued keys, or the absent parent.</returns>
    internal static NestedSetParent<TKey> Read(
        PropertyValues values,
        IProperty property
    ) => s_reader(values, property);

    /// <summary>Copies a present parent independently while preserving the absence of a parent.</summary>
    internal NestedSetParent<TKey> Snapshot(
        IProperty property
    ) => HasValue
        ? new NestedSetParent<TKey>(true, NestedSetTypedValue<TKey>.Snapshot(property, Value))
        : default;

    /// <summary>Checks presence and provider identity without erasing the known parent-key type.</summary>
    internal bool Matches(
        IProperty property,
        NestedSetParent<TKey> expected
    ) => HasValue == expected.HasValue
        && (!HasValue || NestedSetTypedValue<TKey>.Matches(property, Value, expected.Value));

    /// <summary>Builds a typed parent projection for nullable value keys and nullable reference keys.</summary>
    internal static NewExpression Property(
        Expression entity,
        IProperty property
    )
    {
        var access = NestedSetExpressions.Property(entity, property);
        var present = Presence(access);
        var value = Expression.Condition(
            present,
            Expression.Convert(access, typeof(TKey)),
            Expression.Default(typeof(TKey)));

        // WHY: Generic TKey? does not construct Nullable<TKey>. Presence plus the typed value represents both
        // nullable value keys and reference keys without boxing the parent once for every projected node.
        return Expression.New(
            typeof(NestedSetParent<TKey>).GetConstructors()[0],
            present,
            value);
    }

    /// <summary>Tests nullable storage without requiring a custom value key to implement operator !=.</summary>
    internal static Expression Presence(
        Expression access
    ) => Nullable.GetUnderlyingType(access.Type) is not null
        ? Expression.Property(access, nameof(Nullable<>.HasValue))
        : Expression.ReferenceNotEqual(access, Expression.Constant(null, access.Type));

    /// <summary>Compiles the public typed nullable getter once without retaining entries or model metadata.</summary>
    private static Func<PropertyValues, IProperty, NestedSetParent<TKey>> CreateReader()
    {
        var keyType = typeof(TKey);
        var parentType = keyType.IsValueType && Nullable.GetUnderlyingType(keyType) is null
            ? typeof(Nullable<>).MakeGenericType(keyType)
            : keyType;

        var values = Expression.Parameter(typeof(PropertyValues), "values");
        var property = Expression.Parameter(typeof(IProperty), "property");
        var parent = Expression.Variable(parentType, "parent");
        var present = Presence(parent);
        var result = Expression.New(
            typeof(NestedSetParent<TKey>).GetConstructors()[0],
            present,
            Expression.Condition(present, Expression.Convert(parent, keyType), Expression.Default(keyType)));

        // WHY: Generic TKey? does not construct Nullable<TKey>. Resolve the shape once, read it once per call,
        // and test reference identity for null so domain equality operators cannot invent or hide a parent.
        var body = Expression.Block(
            [parent],
            Expression.Assign(parent, Expression.Call(values, nameof(PropertyValues.GetValue), [parentType], property)),
            result);

        return Expression
            .Lambda<Func<PropertyValues, IProperty, NestedSetParent<TKey>>>(body, values, property)
            .Compile();
    }
}

/// <summary>Associates a child with the canonical parent identity resolved using database comparison.</summary>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <param name="Key">The child identity.</param>
/// <param name="Parent">The matching parent's stored identity.</param>
internal readonly record struct NestedSetParentLink<TKey>(
    TKey Key,
    TKey Parent
)
    where TKey : notnull;
