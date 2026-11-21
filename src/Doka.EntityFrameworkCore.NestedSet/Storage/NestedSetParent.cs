namespace Doka.EntityFrameworkCore.NestedSet.Storage;

/// <summary>Keeps optional value-type keys inline in each structural snapshot.</summary>
/// <typeparam name="TKey">The non-null mapped primary key type.</typeparam>
/// <param name="HasValue">Whether the node references a parent.</param>
/// <param name="Value">The parent key when present; otherwise the unused default value.</param>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
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
    internal static MethodCallExpression Property(
        Expression entity,
        IProperty property
    )
    {
        var access = NestedSetExpressions.Property(entity, property);

        // WHY: A converted value key's default can map to SQL NULL. A SQL CASE that substitutes that default
        // then materializes a required TKey throws for roots. Project nullable storage once and unwrap in CLR.
        return Expression.Call(typeof(NestedSetParent<TKey>), nameof(Project), [access.Type], access);
    }

    /// <summary>Creates optional parent storage after EF materializes the mapped nullable value.</summary>
    [UsedImplicitly]
    private static NestedSetParent<TKey> Project<TParent>(
        TParent parent
    ) => ParentProjection<TParent>.Create(parent);

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
            typeof(NestedSetParent<TKey>)
                .GetConstructors()[0],
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

    /// <summary>Retains one typed, allocation-free projection for each exact nullable model shape.</summary>
    private static class ParentProjection<TParent>
    {
        /// <summary>Gets the context-free factory over the exact nullable model value.</summary>
        internal static readonly Func<TParent, NestedSetParent<TKey>> Create = CreateFactory();

        /// <summary>Compiles reference or nullable-value presence without transporting a key through object.</summary>
        private static Func<TParent, NestedSetParent<TKey>> CreateFactory()
        {
            var parent = Expression.Parameter(typeof(TParent), "parent");
            var present = Presence(parent);
            var body = Expression.New(
                typeof(NestedSetParent<TKey>)
                    .GetConstructors()[0],
                present,
                Expression.Condition(
                    present,
                    Expression.Convert(parent, typeof(TKey)),
                    Expression.Default(typeof(TKey))));

            return Expression
                .Lambda<Func<TParent, NestedSetParent<TKey>>>(body, parent)
                .Compile();
        }
    }
}
