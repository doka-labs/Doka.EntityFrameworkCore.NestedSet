namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Tracks one imported node and appends only its owned payload to the bounded batch.</summary>
    private void TrackInsertionGraph(
        Node node,
        int inputIndex,
        List<EntityEntry> batchEntries,
        Dictionary<IUpdateEntry, NestedSetInsertionTracking.Identity> identities,
        List<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships,
        NestedSetCallbackWrites callbackWrites
    )
    {
        if (!_hasInputNavigations)
        {
            // WHY: A model without aggregate navigations owns exactly its root entry. Avoid graph iterator
            // and cycle-set allocations for every node in large imports while preserving the same transition.
            var root = batchEntries[^1];
            callbackWrites.CaptureRelationships(root);
            root.State = EntityState.Added;
            identities.Add(NestedSetInsertionTracking.EntryIdentity(root), NestedSetInsertionTracking.Capture(root));

            return;
        }

        var firstEntry = batchEntries.Count - 1;

        // WHY: A root Tracking callback can observe an owned entry and throw before traversal reaches it.
        // Retain the complete detached aggregate first, then perform transitions; the generic overload also
        // visits caller-tracked owned payload so it can be rejected before fixup changes the caller's aggregate.
        // This visits only the supplied graph, preserving bounded ownership without a tracker-wide scan.
        _store.Context.ChangeTracker.TrackGraph(
            node.Entity,
            new HashSet<IUpdateEntry>(ReferenceEqualityComparer.Instance),
            graphNode =>
            {
                var entry = graphNode.Entry;
                var update = NestedSetInsertionTracking.EntryIdentity(entry);

                if (!graphNode.NodeState.Add(update))
                {
                    return false;
                }

                if (!ReferenceEquals(entry.Entity, node.Entity)
                    && entry.State == EntityState.Detached)
                {
                    // WHY: Traversal has already created this native Detached reference. Own it before
                    // rejecting a deep non-owned navigation, while preserving any caller-tracked foreign row.
                    batchEntries.Add(entry);
                }

                if (!ReferenceEquals(entry.Entity, node.Entity)
                    && !entry.Metadata.IsOwned())
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidImport,
                        "Import entities without populated non-owned navigation properties.");
                }

                if (entry.State != EntityState.Detached)
                {
                    throw new NestedSetException(
                        NestedSetErrorCode.InvalidImport,
                        "Every imported node and its owned payload must be detached.");
                }

                if (graphNode is { SourceEntry: { } owner, InboundNavigation: { } navigation })
                {
                    relationships.Add((owner, navigation, entry.Entity));
                }

                return true;
            });

        // WHY: A malformed deep owned graph must retain its original foreign references when validation fails.
        // Only fully validated input graphs may have their originally empty links reset after tracking fixup.
        _validatedThrough = Math.Max(_validatedThrough, inputIndex + 1);

        // WHY: Capture only entries introduced by the current graph wave before changing their state.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = firstEntry; index < batchEntries.Count; index++)
        {
            var entry = batchEntries[index];

            if (entry.Metadata.IsOwned()
                && NestedSetInsertionValues.Capture(entry.Metadata, entry.Entity) is { } original)
            {
                // WHY: A later wave or final refresh can fail after this owned payload has been detached.
                // Retain its generated and ownership CLR snapshots across waves without EF entry or context state.
                (_ownedValues ??= []).Add(original);
            }
        }

        // WHY: Install identities only for the newly collected suffix after capture is complete.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = firstEntry; index < batchEntries.Count; index++)
        {
            var entry = batchEntries[index];
            callbackWrites.CaptureRelationships(entry);
            entry.State = EntityState.Added;
            identities.Add(NestedSetInsertionTracking.EntryIdentity(entry), NestedSetInsertionTracking.Capture(entry));
        }
    }

    /// <summary>Captures generated identities before EF acceptance and public SavedChanges callbacks.</summary>
    internal void RefreshInsertionIdentities()
    {
        foreach (var identity in (_activeIdentities
            ?? throw new InvalidOperationException("No bulk insertion batch is active.")).Values)
        {
            identity.Refresh();
        }
    }

    /// <summary>Returns a parent that is safe to persist without unresolved identities or navigation fixup.</summary>
    private NestedSetParent<TKey> InitialParent(
        Node node,
        NestedSetParent<TKey> destinationParent,
        int batchOffset
    )
    {
        if (!_parentHasNoNavigations)
        {
            return default;
        }

        if (node.Parent < 0)
        {
            return destinationParent;
        }

        var parent = Nodes[node.Parent];

        return _parentHasDependency && (node.Parent < batchOffset || parent.HasAssignedKey)
            ? new NestedSetParent<TKey>(true, parent.Key)
            : default;
    }

    /// <summary>Recognizes a self-FK whose non-parent components preserve identical partition properties.</summary>
    private static bool IsParentDependency(
        IForeignKey foreignKey,
        IEntityType entityType,
        IProperty parentProperty,
        IProperty keyProperty
    )
    {
        if (foreignKey.PrincipalEntityType != entityType
            || foreignKey.Properties.Count != foreignKey.PrincipalKey.Properties.Count)
        {
            return false;
        }

        var parentIndex = -1;

        // WHY: Retain the ordinal of the parent property to verify its matching principal-key component.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < foreignKey.Properties.Count; index++)
        {
            if (foreignKey.Properties[index] == parentProperty)
            {
                parentIndex = index;
                break;
            }
        }

        if (parentIndex < 0
            || foreignKey.PrincipalKey.Properties[parentIndex] != keyProperty)
        {
            return false;
        }

        // WHY: A scoped self-FK commonly maps (Scope, ParentId) to (Scope, NodeKey). EF can order those
        // inserts because every partition component other than the parent key is the same mapped property.
        // Compare dependent and principal FK components at matching ordinals.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < foreignKey.Properties.Count; index++)
        {
            if (index != parentIndex
                && foreignKey.Properties[index] != foreignKey.PrincipalKey.Properties[index])
            {
                return false;
            }
        }

        return true;
    }
}
