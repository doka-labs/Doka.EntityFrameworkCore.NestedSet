namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Gets every root and owned payload entry permitted through the current guarded insertion save.</summary>
    internal IEnumerable<object> ManagedEntities =>
        (_activeEntries ?? throw new InvalidOperationException("No bulk insertion batch is active.")).Select(entry =>
            entry.Entity);

    /// <summary>Gets only the current bounded wave's native lifecycle handles, or none after detachment.</summary>
    internal IEnumerable<NestedSetInsertionTracking.Identity> ActiveIdentities
        => _activeIdentities is null
            ? []
            : _activeIdentities.Values;

    /// <summary>Stages one bounded insertion batch without retaining EF entries for the complete import.</summary>
    /// <param name="destination">The locked destination before the gap is opened.</param>
    /// <param name="offset">The first preorder node included in this batch.</param>
    /// <param name="count">The bounded number of nodes included in this batch.</param>
    /// <param name="callbackWrites">The complete operation's callback and relationship rollback log.</param>
    /// <param name="cancellationToken">The token checked while assigning the complete input structure.</param>
    internal void StageBatch(
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        int offset,
        int count,
        NestedSetCallbackWrites callbackWrites,
        CancellationToken cancellationToken
    )
    {
        if (offset < 0
            || count <= 0
            || offset > Nodes.Count - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (_activeEntries is not null)
        {
            throw new InvalidOperationException("A bulk insertion batch is already active.");
        }

        var entries = new List<EntityEntry>(count);
        var identities = new Dictionary<IUpdateEntry, NestedSetInsertionTracking.Identity>(count);
        var relationships = new List<(EntityEntry Owner, INavigationBase Navigation, object Entity)>();
        var roots = new EntityEntry<TEntity>[count];

        try
        {
            // WHY: Correlate the active batch root slots with absolute plan indexes.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var relativeIndex = 0; relativeIndex < count; relativeIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = offset + relativeIndex;
                var node = Nodes[index];
                node.CaptureGeneratedValues(_generatedProperties);

                if (_store.Map.Order is not null)
                {
                    var boundaryOffset = checked((long)index * 2);

                    node.Geometry = new Geometry(
                        checked(destination.Boundary + boundaryOffset),
                        checked(destination.Boundary + boundaryOffset + 1),
                        destination.Depth,
                        0);
                }

                // WHY: EF orders assigned keys by modeled FK dependencies. Generated parents from a completed
                // earlier batch can be assigned immediately; unresolved parents in this batch are finalized later.
                // Navigations always defer links so EF fixup cannot mutate the caller-owned object graph.
                node.StagedParent = InitialParent(node, destination.Parent, offset)
                    .Snapshot(_store.Map.ParentProperty);

                node.NeedsParentUpdate = !node.StagedParent.HasValue
                    && (node.Parent >= 0 || destination.Parent.HasValue);

                node.StagedGeometry = node.Geometry;

                // WHY: An already-detached EF entry keeps a strong reference even if a CLR getter or setter
                // fails before tracking. Finish observable CLR access first; only shadow storage requires an entry.
                AssignStagedStructure(node, shadowEntry: null);
                var entry = _store.Entry(node.Entity);
                roots[relativeIndex] = entry;

                // WHY: Shadow assignment can invoke a configured comparer before TrackGraph runs. The entry
                // belongs to this attempt immediately, including when its first transition never completes.
                entries.Add(entry);
                AssignStagedStructure(node, entry);
                TrackInsertionGraph(node, index, entries, identities, relationships, callbackWrites);
            }

            foreach (var identity in identities.Values)
            {
                identity.Refresh();
            }

            _activeOffset = offset;
            _activeRoots = roots;
            _activeEntries = entries.ToArray();
            _activeIdentities = identities;
            _activeRelationships = relationships;
        }
        catch (Exception stageError)
        {
            try
            {
                Detach(entries, identities, relationships);
            }
            catch (Exception detachError)
            {
                // WHY: A state callback can fail while unwinding a partially tracked batch. Preserve the staging
                // failure that triggered cleanup and make the unusable context outcome explicit to the caller.
                throw new AggregateException(
                    "Nested-set import batch cleanup failed. Discard the context.",
                    stageError,
                    detachError);
            }

            throw;
        }
    }

    /// <summary>Assigns CLR structure before entry creation or shadow structure before normal tracking.</summary>
    /// <param name="node">The input whose staged identities and coordinates have been resolved.</param>
    /// <param name="shadowEntry">The detached entry for shadow assignments, or null for CLR assignments.</param>
    private void AssignStagedStructure(
        Node node,
        EntityEntry<TEntity>? shadowEntry
    )
    {
        if (_store.Map.ScopeProperty is { } scope)
        {
            AssignStagedValue(node, shadowEntry, scope, _stagedScope, snapshot: true);
        }

        // WHY: Every import derives TreeId from its typed destination and never trusts detached input state.
        // Mutable identity representations are copied only for their actual CLR or shadow storage destination.
        AssignStagedValue(node, shadowEntry, _store.Map.TreeIdProperty, _stagedTreeId, snapshot: true);

        var parentProperty = _store.Map.ParentProperty;

        if (parentProperty.IsShadowProperty() == shadowEntry is not null)
        {
            var parent = node.StagedParent.Snapshot(parentProperty)
                .BoxedValue;

            if (shadowEntry is null)
            {
                NestedSetStructuralValue.Assign(node.Entity, parentProperty, parent);
            }
            else
            {
                NestedSetStructuralValue.Assign(shadowEntry, parentProperty, parent);
            }
        }

        AssignStagedValue(node, shadowEntry, _store.Map.LeftProperty, node.Geometry.Left);
        AssignStagedValue(node, shadowEntry, _store.Map.RightProperty, node.Geometry.Right);
        AssignStagedValue(node, shadowEntry, _store.Map.DepthProperty, node.Geometry.Depth);
        AssignStagedValue(node, shadowEntry, _store.Map.PositionProperty, node.Geometry.Position);
    }

    /// <summary>Writes one role in its CLR or shadow phase without allocating inactive snapshots.</summary>
    /// <typeparam name="TValue">The known staged identity or coordinate type.</typeparam>
    /// <param name="node">The caller-owned input whose CLR representation is staged.</param>
    /// <param name="shadowEntry">The shadow storage entry, or null for CLR storage.</param>
    /// <param name="property">The exact configured structural role.</param>
    /// <param name="value">The independently resolved staged value.</param>
    /// <param name="snapshot">Whether a mutable identity needs its own storage representation.</param>
    private static void AssignStagedValue<TValue>(
        Node node,
        EntityEntry<TEntity>? shadowEntry,
        IProperty property,
        TValue value,
        bool snapshot = false
    )
        where TValue : notnull
    {
        if (property.IsShadowProperty() != shadowEntry is not null)
        {
            return;
        }

        var assigned = snapshot
            ? NestedSetTypedValue<TValue>.Snapshot(property, value)
            : value;

        if (shadowEntry is null)
        {
            NestedSetStructuralValue.Assign(node.Entity, property, assigned);
        }
        else
        {
            NestedSetStructuralValue.Assign(shadowEntry, property, assigned);
        }
    }
}
