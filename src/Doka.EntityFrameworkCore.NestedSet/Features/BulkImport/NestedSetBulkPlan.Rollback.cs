namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{

    /// <summary>
    ///     Restores caller-owned structure after rollback and leaves application payload under application ownership.
    /// </summary>
    internal void Restore()
    {
        var errors = DetachActiveBatch(failed: true);

        // WHY: Only the validated prefix may have owned navigation links populated by this operation's fixup.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Nodes.Count; index++)
        {
            var node = Nodes[index];
            var type = _store.Map.EntityType.HasSharedClrType
                ? _store.Map.EntityType
                : _store.Context.Model.FindRuntimeEntityType(node.Entity.GetType())!;

            try
            {
                NestedSetInputNavigations.RestoreEmpty(type, node.Entity, includeOwned: index < _validatedThrough);
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }

            var original = node.Original;
            var originalKey = node.HasAssignedKey
                ? node.CurrentKey
                : _store.Map.KeyProperty.Sentinel;

            RestoreValue(
                node,
                _store.Map.KeyProperty,
                original is null
                    ? originalKey
                    : original.KeyOr(originalKey),
                1 << 7,
                ref errors);

            if (_store.Map.ScopeProperty is { } scope)
            {
                RestoreValue(
                    node,
                    scope,
                    original is null
                        ? scope.Sentinel
                        : original.ScopeOr(scope.Sentinel),
                    1 << 0,
                    ref errors);
            }

            RestoreValue(
                node,
                _store.Map.TreeIdProperty,
                original is null
                    ? _store.Map.TreeIdProperty.Sentinel
                    : original.TreeIdOr(_store.Map.TreeIdProperty.Sentinel),
                1 << 1,
                ref errors);

            RestoreValue(
                node,
                _store.Map.ParentProperty,
                original is null
                    ? _store.Map.ParentProperty.Sentinel
                    : original.ParentOr(_store.Map.ParentProperty.Sentinel),
                1 << 2,
                ref errors);

            RestoreValue(
                node,
                _store.Map.LeftProperty,
                original is null
                    ? _store.Map.LeftProperty.Sentinel
                    : original.LeftOr(_store.Map.LeftProperty.Sentinel),
                1 << 3,
                ref errors);

            RestoreValue(
                node,
                _store.Map.RightProperty,
                original is null
                    ? _store.Map.RightProperty.Sentinel
                    : original.RightOr(_store.Map.RightProperty.Sentinel),
                1 << 4,
                ref errors);

            RestoreValue(
                node,
                _store.Map.DepthProperty,
                original is null
                    ? _store.Map.DepthProperty.Sentinel
                    : original.DepthOr(_store.Map.DepthProperty.Sentinel),
                1 << 5,
                ref errors);

            RestoreValue(
                node,
                _store.Map.PositionProperty,
                original is null
                    ? _store.Map.PositionProperty.Sentinel
                    : original.PositionOr(_store.Map.PositionProperty.Sentinel),
                1 << 6,
                ref errors);

            RestoreGeneratedValues(node, ref errors);
        }

        if (_ownedValues is { } ownedValues)
        {
            // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (
                int snapshotIndex = 0, snapshotCount = ownedValues.Count;
                snapshotIndex < snapshotCount;
                snapshotIndex++
            )
            {
                var snapshot = ownedValues[snapshotIndex];

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

        // WHY: Match generated properties, original values and compiled setters by ordinal.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _generatedProperties.Length; index++)
        {
            var property = _generatedProperties[index];

            if (!NestedSetRefreshProperties.AppliesTo(property, node.Entity))
            {
                continue;
            }

            var original = node.OriginalGeneratedValues is { } values
                ? values[index]
                : property.Sentinel;

            try
            {
                // WHY: EF can assign defaults and computed complex leaves before a later failure. The same
                // compiled CLR setter restores their exact snapshots without retaining detached entry handles.
                if (!NestedSetInsertionValues.RestoreDictionaryValue(node.Entity, property, original))
                {
                    _generatedSetters[index]
                        .SetClrValueUsingContainingEntity(
                            node.Entity,
                            NestedSetStructuralValue.Snapshot(property, original));
                }
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
    }

    /// <summary>Restores one exact structural value while retaining errors from other cleanup attempts.</summary>
    private static void RestoreValue(
        Node node,
        IProperty property,
        object? value,
        byte field,
        ref List<Exception>? errors
    )
    {
        try
        {
            var original = node.Original?.IsMissing(field) == true
                ? NestedSetInsertionValues.MissingValue
                : value;

            if (!NestedSetInsertionValues.RestoreDictionaryValue(node.Entity, property, original))
            {
                NestedSetStructuralValue.Assign(
                    node.Entity,
                    property,
                    NestedSetStructuralValue.Snapshot(property, original));
            }
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
    }

    /// <summary>Restores caller-owned aggregate memberships after native detachment changed their links.</summary>
    private static void RestoreRelationships(
        IReadOnlyList<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships,
        ref List<Exception>? errors
    )
    {
        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int itemIndex = 0, itemCount = relationships.Count; itemIndex < itemCount; itemIndex++)
        {
            var (owner, navigation, entity) = relationships[itemIndex];

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
}
