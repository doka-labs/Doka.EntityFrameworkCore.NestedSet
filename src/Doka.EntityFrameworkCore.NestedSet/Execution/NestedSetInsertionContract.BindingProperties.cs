namespace Doka.EntityFrameworkCore.NestedSet.Execution;

internal sealed partial class NestedSetInsertionContract
{
    /// <summary>Closes the exact native PropertySetter contract before insertion registration completes.</summary>
    private Action<IUpdateEntry, IProperty, object?> BindPropertySetter(
        ParameterExpression update,
        ParameterExpression property,
        ParameterExpression value,
        UnaryExpression nativeEntry
    )
    {
        var setProperty = RequireMethod(
            _entryType,
            "SetProperty",
            typeof(void),
            [
                typeof(IPropertyBase),
                typeof(object),
                typeof(bool),
                typeof(bool),
                typeof(bool),
            ]);

        return Expression
            .Lambda<Action<IUpdateEntry, IProperty, object?>>(
                Expression.Call(
                    nativeEntry,
                    setProperty,
                    property,
                    value,
                    Expression.Constant(false),
                    Expression.Constant(false),
                    Expression.Constant(false)),
                update,
                property,
                value)
            .Compile();
    }

    /// <summary>Closes the exact native relationship-snapshot writer before insertion registration completes.</summary>
    private Action<IUpdateEntry, IPropertyBase, object?> BindRelationshipSnapshot(
        ParameterExpression update,
        ParameterExpression value,
        UnaryExpression nativeEntry
    )
    {
        var setRelationship = RequireMethod(
            _entryType,
            "SetRelationshipSnapshotValue",
            typeof(void),
            [typeof(IPropertyBase), typeof(object)]);

        var relationshipProperty = Expression.Parameter(typeof(IPropertyBase), "property");

        // WHY: Scalar setters run FK fixup and cannot restore independently edited relationship snapshots.
        // Bind the existing native boundary once; save/rollback paths must not reflect per entry or navigation.
        return Expression
            .Lambda<Action<IUpdateEntry, IPropertyBase, object?>>(
                Expression.Call(nativeEntry, setRelationship, relationshipProperty, value),
                update,
                relationshipProperty,
                value)
            .Compile();
    }

    /// <summary>Closes the exact native CollectionSnapshots contract before insertion registration completes.</summary>
    private (
        Action<IUpdateEntry, INavigationBase, object> AddCollectionSnapshot,
        Action<IUpdateEntry, INavigationBase, object> RemoveCollectionSnapshot
    ) BindCollectionSnapshots(
        ParameterExpression update,
        ParameterExpression value,
        UnaryExpression nativeEntry
    )
    {
        var navigation = Expression.Parameter(typeof(INavigationBase), "navigation");
        var addCollection = RequireMethod(
            _entryType,
            "AddToCollectionSnapshot",
            typeof(void),
            [typeof(INavigationBase), typeof(object)]);

        var removeCollection = RequireMethod(
            _entryType,
            "RemoveFromCollectionSnapshot",
            typeof(void),
            [typeof(INavigationBase), typeof(object)]);

        // WHY: Collection snapshots have reference-set semantics; the scalar/reference setter is not valid
        // for their slots. Separate native add/remove operations also preserve notification-strategy behavior.
        var boundAddCollectionSnapshot = Expression
            .Lambda<Action<IUpdateEntry, INavigationBase, object>>(
                Expression.Call(nativeEntry, addCollection, navigation, value),
                update,
                navigation,
                value)
            .Compile();

        var boundRemoveCollectionSnapshot = Expression
            .Lambda<Action<IUpdateEntry, INavigationBase, object>>(
                Expression.Call(nativeEntry, removeCollection, navigation, value),
                update,
                navigation,
                value)
            .Compile();

        return (boundAddCollectionSnapshot, boundRemoveCollectionSnapshot);
    }
}
