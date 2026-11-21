namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetInsertionContract
{
    /// <summary>Closes the exact native TrackingLifecycle contract before insertion registration completes.</summary>
    private (
        Action<object, IUpdateEntry, EntityState> StopTracking,
        Action<object, IUpdateEntry, EntityState, EntityState?> UpdateReferences
    ) BindTrackingLifecycle(
        Type managerType,
        ParameterExpression update,
        ParameterExpression manager,
        UnaryExpression nativeEntry,
        UnaryExpression nativeManager
    )
    {
        var state = Expression.Parameter(typeof(EntityState), "state");
        var oldState = Expression.Parameter(typeof(EntityState?), "oldState");
        var stop = RequireMethod(managerType, "StopTracking", typeof(void), [_entryType, typeof(EntityState)]);
        var references = RequireMethod(
            managerType,
            "UpdateReferenceMaps",
            typeof(void),
            [_entryType, typeof(EntityState), typeof(EntityState?)]);

        var boundStopTracking = Expression
            .Lambda<Action<object, IUpdateEntry, EntityState>>(
                Expression.Call(nativeManager, stop, nativeEntry, state),
                manager,
                update,
                state)
            .Compile();

        var boundUpdateReferences = Expression
            .Lambda<Action<object, IUpdateEntry, EntityState, EntityState?>>(
                Expression.Call(nativeManager, references, nativeEntry, state, oldState),
                manager,
                update,
                state,
                oldState)
            .Compile();

        return (boundStopTracking, boundUpdateReferences);
    }

    /// <summary>Closes the exact native ClrSetter contract before insertion registration completes.</summary>
    private Func<IPropertyBase, IClrPropertySetter> BindClrSetter()
    {
        var runtimeProperty = RequireType("Microsoft.EntityFrameworkCore.Metadata.Internal.IRuntimePropertyBase");
        var getSetter = RequireMethod(runtimeProperty, "GetSetter", typeof(IClrPropertySetter), []);
        var metadata = Expression.Parameter(typeof(IPropertyBase), "metadata");
        _ = RequireMethod(
            typeof(IClrPropertySetter),
            nameof(IClrPropertySetter.SetClrValueUsingContainingEntity),
            typeof(void),
            [typeof(object), typeof(object)]);

        return Expression
            .Lambda<Func<IPropertyBase, IClrPropertySetter>>(
                Expression.Call(Expression.Convert(metadata, runtimeProperty), getSetter),
                metadata)
            .Compile();
    }

    /// <summary>Closes the exact native KeyMapSignatures contract before insertion registration completes.</summary>
    private void ValidateKeyMapSignatures()
    {
        // WHY: Resolving only non-generic members would defer inherited protected map/factory failures until
        // a first insert. Validate value, reference, mutable, compound and nullable map signatures here;
        // only model-consumed map types need compiled operations in the weak type cache.
        Type[] keyTypes =
        [
            typeof(int),
            typeof(Guid),
            typeof(string),
            typeof(byte[]),
            typeof(IReadOnlyList<object>),
        ];

        foreach (var definition in new[] { NativeType("IdentityMap`1"), NativeType("NullableKeyIdentityMap`1") })
        {
            foreach (var keyType in keyTypes)
            {
                _ = RequireKeyMap(definition.MakeGenericType(keyType));
            }
        }
    }
}
