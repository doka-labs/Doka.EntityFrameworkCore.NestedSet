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
        var errors = DetachActiveBatch(failed: true);

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

        if (_ownedValues is { } ownedValues)
        {
            foreach (var snapshot in ownedValues)
            {
                try
                {
                    snapshot.Restore();
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
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
        if (!node.GeneratedValuesCaptured
            || _generatedProperties.Length == 0)
        {
            // WHY: A null snapshot denotes sentinels only after capture completed. Inputs in a later batch,
            // or an import rejected before staging, still own their untouched generated CLR payload values.
            return;
        }

        for (var index = 0; index < _generatedProperties.Length; index++)
        {
            var property = _generatedProperties[index];

            if (!NestedSetRefreshProperties.AppliesTo(property, node.Entity))
            {
                continue;
            }

            var original = node.OriginalGeneratedValues is { } values ? values[index] : property.Sentinel;

            try
            {
                // WHY: EF can assign defaults and computed complex leaves before a later failure. The same
                // compiled CLR setter restores their exact snapshots without retaining detached entry handles.
                _generatedSetters[index].SetClrValueUsingContainingEntity(
                    node.Entity,
                    NestedSetStructuralValue.Snapshot(property, original));
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
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
    private List<Exception>? DetachActiveBatch(bool failed = false)
    {
        if (_activeEntries is not { } entries)
        {
            return null;
        }

        List<Exception>? errors = null;
        Detach(entries, _activeIdentities!, ref errors);

        if (failed)
        {
            VerifyFailedCleanup(entries, ref errors);
        }

        if (_activeRelationships is { } relationships)
        {
            RestoreRelationships(relationships, ref errors);
        }

        if (errors is not null)
        {
            // WHY: StateChanging can reject detachment before EF changes the entry state. Keep the bounded
            // batch handles available for rollback cleanup instead of losing ownership of still-tracked rows.
            return errors;
        }

        _activeEntries = null;
        _activeIdentities = null;
        _activeRelationships = null;
        _activeRoots = null;
        _activeOffset = 0;

        return null;
    }

    /// <summary>Detaches a bounded entry set in reverse dependency order.</summary>
    private static void Detach(
        IReadOnlyList<EntityEntry> entries,
        IReadOnlyDictionary<IUpdateEntry, NestedSetInsertionTracking.Identity> identities,
        ref List<Exception>? errors
    )
    {
        foreach (var entry in entries)
        {
            if (identities.TryGetValue(NestedSetInsertionTracking.EntryIdentity(entry), out var identity))
            {
                try
                {
                    identity.PrepareDetach();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
        }

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            try
            {
                if (identities.TryGetValue(NestedSetInsertionTracking.EntryIdentity(entries[index]), out var identity))
                {
                    identity.Detach();
                }
                else
                {
                    NestedSetInsertionTracking.Detach(entries[index]);
                }
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
    }

    /// <summary>Detaches a partial batch while preserving its original exception.</summary>
    private static void Detach(
        IReadOnlyList<EntityEntry> entries,
        IReadOnlyDictionary<IUpdateEntry, NestedSetInsertionTracking.Identity> identities,
        IReadOnlyList<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships
    )
    {
        List<Exception>? errors = null;
        Detach(entries, identities, ref errors);
        VerifyFailedCleanup(entries, ref errors);
        RestoreRelationships(relationships, ref errors);

        if (errors is not null)
        {
            throw new AggregateException("One or more imported entities could not be detached.", errors);
        }
    }

    /// <summary>Checks only the failed batch's touched native maps without replacing its original error.</summary>
    private static void VerifyFailedCleanup(IReadOnlyList<EntityEntry> entries, ref List<Exception>? errors)
    {
        try
        {
            NestedSetInsertionTracking.VerifyFailedCleanup(entries);
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }
    }

    /// <summary>Restores caller-owned aggregate memberships after native detachment changed their links.</summary>
    private static void RestoreRelationships(
        IReadOnlyList<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships,
        ref List<Exception>? errors
    )
    {
        foreach (var (owner, navigation, entity) in relationships)
        {
            try
            {
                NestedSetInsertionTracking.RestoreRelationship(owner.Entity, navigation, entity);
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
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
