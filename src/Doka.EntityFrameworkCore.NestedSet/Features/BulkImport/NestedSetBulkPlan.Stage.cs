namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Stages one bounded insertion batch without retaining EF entries for the complete import.</summary>
    /// <param name="destination">The locked destination before the gap is opened.</param>
    /// <param name="offset">The first preorder node included in this batch.</param>
    /// <param name="count">The bounded number of nodes included in this batch.</param>
    /// <param name="cancellationToken">The token checked while assigning the complete input structure.</param>
    internal void StageBatch(
        NestedSetPlacementResolver<TEntity, TKey, TTreeId, TScope>.Destination destination,
        int offset,
        int count,
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
                TrackInsertionGraph(node, entries, identities, relationships);
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

        if (parentProperty.IsShadowProperty() == (shadowEntry is not null))
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
        if (property.IsShadowProperty() != (shadowEntry is not null))
        {
            return;
        }

        var assigned = snapshot ? NestedSetTypedValue<TValue>.Snapshot(property, value) : value;

        if (shadowEntry is null)
        {
            NestedSetStructuralValue.Assign(node.Entity, property, assigned);
        }
        else
        {
            NestedSetStructuralValue.Assign(shadowEntry, property, assigned);
        }
    }

    /// <summary>Tracks one imported node and appends only its owned payload to the bounded batch.</summary>
    private void TrackInsertionGraph(
        Node node,
        List<EntityEntry> batchEntries,
        Dictionary<IUpdateEntry, NestedSetInsertionTracking.Identity> identities,
        List<(EntityEntry Owner, INavigationBase Navigation, object Entity)> relationships
    )
    {
        if (!_hasInputNavigations)
        {
            // WHY: A model without aggregate navigations owns exactly its root entry. Avoid graph iterator
            // and cycle-set allocations for every node in large imports while preserving the same transition.
            var root = batchEntries[^1];
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

        for (var index = firstEntry; index < batchEntries.Count; index++)
        {
            var entry = batchEntries[index];
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

    /// <summary>Gets every root and owned payload entry permitted through the current guarded insertion save.</summary>
    internal IEnumerable<object> ManagedEntities =>
        (_activeEntries ?? throw new InvalidOperationException("No bulk insertion batch is active.")).Select(entry =>
            entry.Entity);

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

    /// <summary>Rejects save callbacks that change the managed topology or add unplanned hierarchy entities.</summary>
    internal void RequireSavedStage()
    {
        var roots = _activeRoots ?? throw new InvalidOperationException("No bulk insertion batch is active.");

        var currentRoots = roots
            .Select(entry => entry.Entity)
            .ToHashSet<TEntity>(ReferenceEqualityComparer.Instance);

        RequireSavedMembership(_store.Context, _store.Map.EntityType, _knownTracked, currentRoots);
        RequireSavedGeometry();
    }

    /// <summary>Rejects membership changes without repeating global detection after a guarded insertion save.</summary>
    /// <param name="context">The context whose exact hierarchy membership is checked.</param>
    /// <param name="entityType">The exact mapped hierarchy type, including its derived entities.</param>
    /// <param name="knownTracked">The immutable pre-import identity baseline.</param>
    /// <param name="expected">The hierarchy entities owned by the current bounded payload wave.</param>
    internal static void RequireSavedMembership(
        DbContext context,
        IEntityType entityType,
        HashSet<TEntity> knownTracked,
        HashSet<TEntity> expected
    )
    {
        var knownCount = 0;
        var stagedCount = 0;
        var tracker = context.ChangeTracker;
        var automaticDetection = tracker.AutoDetectChangesEnabled;

        try
        {
            // WHY: EF detects callback payload before persistence. This scan checks identity membership only;
            // automatic detection would rescan every unrelated entry for each before/after-save guard call.
            tracker.AutoDetectChangesEnabled = false;

            foreach (var entry in NestedSetEntityAccess<TEntity>.Entries(context, entityType))
            {
                if (entry.State == EntityState.Detached)
                {
                    continue;
                }

                if (knownTracked.Contains(entry.Entity))
                {
                    knownCount++;

                    continue;
                }

                if (expected.Contains(entry.Entity))
                {
                    stagedCount++;

                    continue;
                }

                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Save callbacks must not add or remove hierarchy entities during an import.");
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = automaticDetection;
        }

        if (knownCount != knownTracked.Count
            || stagedCount != expected.Count)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidImport,
                "Save callbacks must not add or remove hierarchy entities during an import.");
        }
    }

    /// <summary>Validates this plan's staged scalar structure after the shared wave validates membership.</summary>
    internal void RequireSavedGeometry()
    {
        var roots = _activeRoots ?? throw new InvalidOperationException("No bulk insertion batch is active.");

        for (var relativeIndex = 0; relativeIndex < roots.Length; relativeIndex++)
        {
            var node = Nodes[_activeOffset + relativeIndex];
            var entry = roots[relativeIndex];
            var values = entry.CurrentValues;

            // WHY: The managed save bypasses ordinary direct-write rejection. Validate its exact staged structure
            // before subsequent SQL or refresh could conceal a callback's unauthorized hierarchy changes.
            if (entry.State == EntityState.Detached
                || !_activeIdentities![NestedSetInsertionTracking.EntryIdentity(entry)]
                    .Matches(_store.Map.KeyProperty)
                || (node.HasAssignedKey
                    && !NestedSetTypedValue<TKey>.Matches(
                        _store.Map.KeyProperty,
                        NestedSetTypedValue<TKey>.Read(values, _store.Map.KeyProperty),
                        node.CurrentKey))
                || (_store.Map.ScopeProperty is { } scope
                    && !NestedSetTypedValue<TScope>.Matches(
                        scope,
                        NestedSetTypedValue<TScope>.Read(values, scope),
                        _stagedScope))
                || !NestedSetTypedValue<TTreeId>.Matches(
                    _store.Map.TreeIdProperty,
                    NestedSetTypedValue<TTreeId>.Read(values, _store.Map.TreeIdProperty),
                    _stagedTreeId)
                || !NestedSetParent<TKey>
                    .Read(values, _store.Map.ParentProperty)
                    .Matches(_store.Map.ParentProperty, node.StagedParent)
                || NestedSetTypedValue<long>.Read(values, _store.Map.LeftProperty) != node.StagedGeometry.Left
                || NestedSetTypedValue<long>.Read(values, _store.Map.RightProperty) != node.StagedGeometry.Right
                || NestedSetTypedValue<int>.Read(values, _store.Map.DepthProperty) != node.StagedGeometry.Depth
                || NestedSetTypedValue<long>.Read(values, _store.Map.PositionProperty) != node.StagedGeometry.Position)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Save callbacks must not alter managed hierarchy values during an import.");
            }
        }
    }

    /// <summary>Captures generated identities and releases all EF state owned by the current insertion batch.</summary>
    internal void CompleteBatch()
    {
        var roots = _activeRoots ?? throw new InvalidOperationException("No bulk insertion batch is active.");

        for (var relativeIndex = 0; relativeIndex < roots.Length; relativeIndex++)
        {
            var node = Nodes[_activeOffset + relativeIndex];

            if (!node.HasAssignedKey)
            {
                // WHY: Assigned identities are immutable rollback snapshots. Only EF-generated identities
                // replace the captured key after persistence, so a callback cannot redefine the requested node.
                node.CurrentKey = NestedSetTypedValue<TKey>.Snapshot(
                    _store.Map.KeyProperty,
                    NestedSetTypedValue<TKey>.Read(roots[relativeIndex], _store.Map.KeyProperty));
            }
        }

        if (DetachActiveBatch() is { } errors)
        {
            // WHY: A failed detach callback is an insertion failure too. The executor must roll back the wave,
            // and retained entry handles let restoration retry every aggregate that could not be detached.
            throw new AggregateException("One or more imported entities could not be detached.", errors);
        }
    }
}
