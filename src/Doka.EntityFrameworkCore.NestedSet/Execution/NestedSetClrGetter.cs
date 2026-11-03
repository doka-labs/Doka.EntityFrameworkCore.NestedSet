namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Shares metadata-selected typed CLR getters without reading temporary or generated sidecars.</summary>
/// <typeparam name="TValue">The exact mapped model type, including nullable and converted values.</typeparam>
internal static class NestedSetClrGetter<TValue>
{
    private static readonly ConditionalWeakTable<IProperty, Func<object, TValue>> s_getters = new();

    /// <summary>Resolves a model-lifetime getter for a mapped non-shadow property.</summary>
    /// <param name="property">The finalized property whose CLR storage is read independently of EF sidecars.</param>
    /// <returns>The shared getter, retaining EF semantics for complex paths and differing member/model types.</returns>
    internal static Func<object, TValue> For(
        IProperty property
    ) => s_getters.GetValue(property, Create);

    /// <summary>Uses public member selection for scalars and EF's exact getter for exceptional paths.</summary>
    private static Func<object, TValue> Create(
        IProperty property
    )
    {
        var member = property.GetMemberInfo(forMaterialization: false, forSet: false);

        if (property.DeclaringType is not IEntityType declaring
            || !member.DeclaringType!.IsAssignableFrom(declaring.ClrType))
        {
            // WHY: EF's public CLR getter returns object. Complex containing paths, ordinals and proxy-only
            // members retain that exact framework route rather than duplicating its access semantics. Boxing
            // remains at this exceptional boundary; ordinary mapped fields, properties and indexers stay typed.
            var getter = property.GetGetter();

            return input => (TValue)getter.GetClrValueUsingContainingEntity(input)!;
        }

        var entity = Expression.Parameter(typeof(object), "entity");
        var instance = Expression.Convert(entity, declaring.ClrType);
        Expression read = member switch
        {
            FieldInfo field => Expression.Field(instance, field),
            PropertyInfo { GetMethod: { } getter } when property.IsIndexerProperty() => Expression.Call(
                instance,
                getter,
                Expression.Constant(property.Name)),
            PropertyInfo scalar => Expression.Property(instance, scalar),
            _ => throw new InvalidOperationException("The mapped scalar has no readable CLR member."),
        };

        if (read.Type != typeof(TValue))
        {
            // WHY: EF applies configured sentinel semantics before converting differing member/model types.
            // A direct cast or null-only conversion would change custom sentinels. Preserve its public getter;
            // boxing at this uncommon boundary is separate from avoidable ordinary scalar boxes.
            var getter = property.GetGetter();

            return input => (TValue)getter.GetClrValueUsingContainingEntity(input)!;
        }

        return Expression
            .Lambda<Func<object, TValue>>(read, entity)
            .Compile();
    }
}
