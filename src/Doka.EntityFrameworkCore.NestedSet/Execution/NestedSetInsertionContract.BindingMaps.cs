namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetInsertionContract
{
    /// <summary>Closes the exact native IdentityMap contract before insertion registration completes.</summary>
    private Func<object, IKey, object?> BindIdentityMap(
        Type managerType,
        ParameterExpression manager,
        UnaryExpression nativeManager
    )
    {
        var key = Expression.Parameter(typeof(IKey), "key");
        var findMap = RequireMethod(managerType, "FindIdentityMap", NativeType("IIdentityMap"), [typeof(IKey)]);

        return Expression
            .Lambda<Func<object, IKey, object?>>(
                Expression.Convert(Expression.Call(nativeManager, findMap, key), typeof(object)),
                manager,
                key)
            .Compile();
    }

    /// <summary>Closes the exact native DependentMaps contract before insertion registration completes.</summary>
    private (
        Func<object, IReadOnlyList<object?>, IEnumerable<IUpdateEntry>> Dependents,
        Func<object, IReadOnlyList<object?>, IForeignKey, IEnumerable<IUpdateEntry>> GetOrCreateDependents,
        Action<object, IUpdateEntry, IForeignKey> UpdateDependentMap
    ) BindDependentMaps(
        Type managerType,
        ParameterExpression update,
        ParameterExpression manager,
        UnaryExpression nativeEntry,
        UnaryExpression nativeManager
    )
    {
        var map = Expression.Parameter(typeof(object), "map");
        var values = Expression.Parameter(typeof(IReadOnlyList<object>), "values");
        var dependentType = NativeType("IDependentsMap");
        var getDependents = RequireMethod(
            dependentType,
            "GetDependents",
            typeof(IEnumerable<IUpdateEntry>),
            [typeof(IReadOnlyList<object>)]);

        var boundDependents = Expression
            .Lambda<Func<object, IReadOnlyList<object?>, IEnumerable<IUpdateEntry>>>(
                Expression.Call(Expression.Convert(map, dependentType), getDependents, values),
                map,
                values)
            .Compile();

        var foreignKey = Expression.Parameter(typeof(IForeignKey), "foreignKey");
        var getOrCreateDependents = RequireMethod(
            managerType,
            "GetDependents",
            typeof(IEnumerable<IUpdateEntry>),
            [typeof(IReadOnlyList<object>), typeof(IForeignKey)]);

        // WHY: A missing lazy map is not proof of absent membership. EF initializes it from its own entries;
        // capturing that actual membership avoids guessing from raw keys or conceptual-null flags.
        var boundGetOrCreateDependents =
            Expression
            .Lambda<Func<object, IReadOnlyList<object?>, IForeignKey, IEnumerable<IUpdateEntry>>>(
                Expression.Call(nativeManager, getOrCreateDependents, values, foreignKey),
                manager,
                values,
                foreignKey)
            .Compile();

        var boundUpdateDependentMap = Expression
            .Lambda<Action<object, IUpdateEntry, IForeignKey>>(
                Expression.Call(
                    nativeManager,
                    RequireMethod(managerType, "UpdateDependentMap", typeof(void), [_entryType, typeof(IForeignKey)]),
                    nativeEntry,
                    foreignKey),
                manager,
                update,
                foreignKey)
            .Compile();

        return (boundDependents, boundGetOrCreateDependents, boundUpdateDependentMap);
    }

    /// <summary>Closes the exact native ChangedCount contract before insertion registration completes.</summary>
    private (
        Func<object, int> GetChangedCount,
        Action<object, int> SetChangedCount
    ) BindChangedCount(
        Type managerType,
        ParameterExpression manager,
        UnaryExpression nativeManager
    )
    {
        var changedCount = Expression.Parameter(typeof(int), "changedCount");
        var boundGetChangedCount = Expression
            .Lambda<Func<object, int>>(
                Expression.Call(nativeManager, RequireMethod(managerType, "get_ChangedCount", typeof(int), [])),
                manager)
            .Compile();

        var boundSetChangedCount = Expression
            .Lambda<Action<object, int>>(
                Expression.Call(
                    nativeManager,
                    RequireMethod(managerType, "set_ChangedCount", typeof(void), [typeof(int)]),
                    changedCount),
                manager,
                changedCount)
            .Compile();

        return (boundGetChangedCount, boundSetChangedCount);
    }
}
