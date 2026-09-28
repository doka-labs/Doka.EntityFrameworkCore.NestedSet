namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Detaches only entities owned by this import, retaining unrelated unchanged tracked entries.</summary>
    internal void Detach()
    {
        if (DetachActiveBatch() is { } errors)
        {
            throw new AggregateException("One or more imported entities could not be detached.", errors);
        }
    }

    /// <summary>
    ///     Restores caller-owned structure after rollback and leaves application payload under application ownership.
    /// </summary>
    internal void Restore()
    {
        var errors = DetachActiveBatch();

        foreach (var node in Nodes)
        {
            var original = node.Original;
            var originalKey = node.HasAssignedKey ? node.CurrentKey : _store.Map.KeyProperty.Sentinel;
            RestoreValue(node.Entity, _store.Map.KeyProperty, originalKey, ref errors);

            if (_store.Map.ScopeProperty is { } scope)
            {
                RestoreValue(
                    node.Entity,
                    scope,
                    original is null ? scope.Sentinel : original.ScopeOr(scope.Sentinel),
                    ref errors);
            }

            RestoreValue(
                node.Entity,
                _store.Map.TreeIdProperty,
                original is null
                    ? _store.Map.TreeIdProperty.Sentinel
                    : original.TreeIdOr(_store.Map.TreeIdProperty.Sentinel),
                ref errors);

            RestoreValue(
                node.Entity,
                _store.Map.ParentProperty,
                original is null
                    ? _store.Map.ParentProperty.Sentinel
                    : original.ParentOr(_store.Map.ParentProperty.Sentinel),
                ref errors);

            RestoreValue(
                node.Entity,
                _store.Map.LeftProperty,
                original is null ? _store.Map.LeftProperty.Sentinel : original.LeftOr(_store.Map.LeftProperty.Sentinel),
                ref errors);

            RestoreValue(
                node.Entity,
                _store.Map.RightProperty,
                original is null
                    ? _store.Map.RightProperty.Sentinel
                    : original.RightOr(_store.Map.RightProperty.Sentinel),
                ref errors);

            RestoreValue(
                node.Entity,
                _store.Map.DepthProperty,
                original is null
                    ? _store.Map.DepthProperty.Sentinel
                    : original.DepthOr(_store.Map.DepthProperty.Sentinel),
                ref errors);

            RestoreValue(
                node.Entity,
                _store.Map.PositionProperty,
                original is null
                    ? _store.Map.PositionProperty.Sentinel
                    : original.PositionOr(_store.Map.PositionProperty.Sentinel),
                ref errors);

            RestoreGeneratedValues(node, ref errors);
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more imported entities could not be restored.", errors);
        }
    }

    /// <summary>Restores provider-generated CLR and complex values without retaining EF tracking state.</summary>
    private void RestoreGeneratedValues(
        Node node,
        ref List<Exception>? errors
    )
    {
        if (_generatedProperties.Length == 0)
        {
            return;
        }

        var entry = _store.Entry(node.Entity);

        for (var index = 0; index < _generatedProperties.Length; index++)
        {
            var property = _generatedProperties[index];
            var original = node.OriginalGeneratedValues is { } values ? values[index] : property.Sentinel;

            try
            {
                // WHY: EF can write defaults and computed complex leaves into caller-owned inputs before a later
                // refresh failure. CurrentValues resolves the complete complex path while the entry remains detached.
                entry.CurrentValues[property] = NestedSetStructuralValue.Snapshot(property, original);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        try
        {
            entry.State = EntityState.Detached;
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
    }

    /// <summary>Restores one exact structural value while retaining errors from other cleanup attempts.</summary>
    private static void RestoreValue(
        TEntity entity,
        IProperty property,
        object? value,
        ref List<Exception>? errors
    )
    {
        try
        {
            NestedSetStructuralValue.Assign(entity, property, NestedSetStructuralValue.Snapshot(property, value));
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
    }

    /// <summary>Attempts every owned detachment and retains failures for the operation boundary to aggregate.</summary>
    private List<Exception>? DetachActiveBatch()
    {
        if (_activeEntries is not { } entries)
        {
            return null;
        }

        List<Exception>? errors = null;
        Detach(entries, ref errors);

        if (errors is not null)
        {
            // WHY: StateChanging can reject detachment before EF changes the entry state. Keep the bounded
            // batch handles available for rollback cleanup instead of losing ownership of still-tracked rows.
            return errors;
        }

        _activeEntries = null;
        _activeRoots = null;
        _activeOffset = 0;

        return null;
    }

    /// <summary>Detaches a bounded entry set in reverse dependency order.</summary>
    private static void Detach(
        IReadOnlyList<EntityEntry> entries,
        ref List<Exception>? errors
    )
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            try
            {
                entries[index].State = EntityState.Detached;
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
    }

    /// <summary>Detaches a partial batch while preserving its original exception.</summary>
    private static void Detach(
        IReadOnlyList<EntityEntry> entries
    )
    {
        List<Exception>? errors = null;
        Detach(entries, ref errors);

        if (errors is not null)
        {
            throw new AggregateException("One or more imported entities could not be detached.", errors);
        }
    }

    /// <summary>
    ///     Rejects tracking and navigation graphs that would introduce entities outside the explicit topology.
    /// </summary>
    private static void RequireDetached(
        TEntity entity,
        HashSet<TEntity> tracked,
        IEntityType entityType
    )
    {
        if (tracked.Contains(entity))
        {
            throw new NestedSetException(NestedSetErrorCode.InvalidImport, "Every imported entity must be detached.");
        }

        foreach (var navigation in entityType
                     .GetNavigations()
                     .Cast<INavigationBase>()
                     .Concat(entityType.GetSkipNavigations()))
        {
            var value = navigation
                .GetGetter()
                .GetClrValue(entity);

            if (value is not null
                && (value is not IEnumerable sequence
                    || sequence
                        .Cast<object>()
                        .Any())
                && !navigation.TargetEntityType.IsOwned())
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Import entities without populated non-owned navigation properties.");
            }
        }
    }
}
