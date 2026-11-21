using System.Collections.Specialized;
using System.ComponentModel;

namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetInsertionContract
{
    /// <summary>Closes the exact native Entry contract before insertion registration completes.</summary>
    private static Func<EntityEntry, IUpdateEntry> BindEntry(
        Type infrastructure,
        MethodInfo entryGetter,
        ParameterExpression entry
    )
    {
        return Expression
            .Lambda<Func<EntityEntry, IUpdateEntry>>(
                Expression.Convert(
                    Expression.Call(Expression.Convert(entry, infrastructure), entryGetter),
                    typeof(IUpdateEntry)),
                entry)
            .Compile();
    }

    /// <summary>Closes the exact native Manager contract before insertion registration completes.</summary>
    private static Func<IUpdateEntry, object> BindManager(
        MethodInfo managerGetter,
        ParameterExpression update,
        UnaryExpression nativeEntry
    )
    {
        return Expression
            .Lambda<Func<IUpdateEntry, object>>(
                Expression.Convert(Expression.Call(nativeEntry, managerGetter), typeof(object)),
                update)
            .Compile();
    }

    /// <summary>Closes the exact native Notifications contract before insertion registration completes.</summary>
    private (
        Func<IUpdateEntry, PropertyChangingEventHandler> PropertyChangingHandler,
        Func<IUpdateEntry, PropertyChangedEventHandler> PropertyChangedHandler,
        Func<IUpdateEntry, NotifyCollectionChangedEventHandler> CollectionChangedHandler
    ) BindNotifications()
    {
        var changing = RequireMethod(
            _entryType,
            "HandleINotifyPropertyChanging",
            typeof(void),
            [typeof(object), typeof(PropertyChangingEventArgs)]);

        var changed = RequireMethod(
            _entryType,
            "HandleINotifyPropertyChanged",
            typeof(void),
            [typeof(object), typeof(PropertyChangedEventArgs)]);

        var collectionChanged = RequireMethod(
            _entryType,
            "HandleINotifyCollectionChanged",
            typeof(void),
            [typeof(object), typeof(NotifyCollectionChangedEventArgs)]);

        // WHY: AutoDetectChanges=false does not pause notification fixup. Subscriber service methods call
        // GetOrCreateCollection and would replace a legitimate null collection while rollback restores it.
        // Close the validated native method directly, so event -= matches EF's original method/target pair.
        // Delegate creation is failure-only; neither reflection lookup nor a wrapper invocation runs per row.
        Func<IUpdateEntry, PropertyChangingEventHandler> boundPropertyChangingHandler =
            changing.CreateDelegate<PropertyChangingEventHandler>;
        Func<IUpdateEntry, PropertyChangedEventHandler> boundPropertyChangedHandler =
            changed.CreateDelegate<PropertyChangedEventHandler>;
        Func<IUpdateEntry, NotifyCollectionChangedEventHandler> boundCollectionChangedHandler =
            collectionChanged.CreateDelegate<NotifyCollectionChangedEventHandler>;

        return (boundPropertyChangingHandler, boundPropertyChangedHandler, boundCollectionChangedHandler);
    }

    /// <summary>Closes the exact native TrackedEntry contract before insertion registration completes.</summary>
    private Func<object, object, IEntityType, IUpdateEntry?> BindTrackedEntry(
        Type managerType,
        ParameterExpression manager,
        UnaryExpression nativeManager
    )
    {
        var entity = Expression.Parameter(typeof(object), "entity");
        var entityType = Expression.Parameter(typeof(IEntityType), "entityType");
        var findEntry = RequireMethod(
            managerType,
            "TryGetEntry",
            _entryType,
            [typeof(object), typeof(IEntityType), typeof(bool)]);

        // WHY: IUpdateAdapter has no reference lookup. Reuse EF's own side-effect-free navigation-fixup lookup
        // instead of building an O(n) tracked-entry index whenever one relationship changes.
        return Expression
            .Lambda<Func<object, object, IEntityType, IUpdateEntry?>>(
                Expression.Convert(
                    Expression.Call(nativeManager, findEntry, entity, entityType, Expression.Constant(true)),
                    typeof(IUpdateEntry)),
                manager,
                entity,
                entityType)
            .Compile();
    }
}
