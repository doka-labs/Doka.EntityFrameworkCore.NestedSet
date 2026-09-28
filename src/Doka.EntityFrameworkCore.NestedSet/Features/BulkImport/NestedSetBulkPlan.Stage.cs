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
        var roots = new EntityEntry<TEntity>[count];

        try
        {
            for (var relativeIndex = 0; relativeIndex < count; relativeIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = offset + relativeIndex;
                var node = Nodes[index];
                var entry = _store.Entry(node.Entity);
                roots[relativeIndex] = entry;
                node.CaptureGeneratedValues(entry, _generatedProperties);

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
                if (_store.Map.ScopeProperty is { } scope)
                {
                    // WHY: An input byte array must not alias the expected snapshot or the facade's Scope argument.
                    NestedSetStructuralValue.Assign(
                        entry,
                        scope,
                        NestedSetTypedValue<TScope>.Snapshot(scope, _stagedScope));
                }

                // WHY: Every import derives TreeId from its typed destination and never trusts detached input state.
                NestedSetStructuralValue.Assign(
                    entry,
                    _store.Map.TreeIdProperty,
                    NestedSetTypedValue<TTreeId>.Snapshot(_store.Map.TreeIdProperty, _stagedTreeId));

                NestedSetStructuralValue.Assign(
                    entry,
                    _store.Map.ParentProperty,
                    node.StagedParent.Snapshot(_store.Map.ParentProperty)
                        .BoxedValue);

                NestedSetStructuralValue.Assign(entry, _store.Map.LeftProperty, node.Geometry.Left);
                NestedSetStructuralValue.Assign(entry, _store.Map.RightProperty, node.Geometry.Right);
                NestedSetStructuralValue.Assign(entry, _store.Map.DepthProperty, node.Geometry.Depth);
                NestedSetStructuralValue.Assign(entry, _store.Map.PositionProperty, node.Geometry.Position);
                TrackInsertionGraph(node, entries);
            }

            _activeOffset = offset;
            _activeRoots = roots;
            _activeEntries = entries.ToArray();
        }
        catch (Exception stageError)
        {
            try
            {
                Detach(entries);
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

    /// <summary>Tracks one imported node and appends only its owned payload to the bounded batch.</summary>
    private void TrackInsertionGraph(
        Node node,
        List<EntityEntry> batchEntries
    )
    {
        var introducedStart = batchEntries.Count;

        try
        {
            // WHY: Enumerating the complete ChangeTracker before and after every node makes a bulk import
            // quadratic. TrackGraph visits only this detached aggregate and exposes its exact introduced entries.
            _store.Context.ChangeTracker.TrackGraph(
                node.Entity,
                graphNode =>
                {
                    var entry = graphNode.Entry;
                    if (!ReferenceEquals(entry.Entity, node.Entity)
                        && !entry.Metadata.IsOwned())
                    {
                        throw new NestedSetException(
                            NestedSetErrorCode.InvalidImport,
                            "Import entities without populated non-owned navigation properties.");
                    }

                    batchEntries.Add(entry);
                    entry.State = EntityState.Added;
                });
        }
        catch
        {
            // WHY: A graph callback can fail after EF has started tracking the aggregate. Detaching the exact
            // partial graph preserves the documented reusable-input contract without scanning unrelated entries.
            for (var index = batchEntries.Count - 1; index >= introducedStart; index--)
            {
                batchEntries[index].State = EntityState.Detached;
                batchEntries.RemoveAt(index);
            }

            throw;
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
            node.CurrentKey = NestedSetTypedValue<TKey>.Snapshot(
                _store.Map.KeyProperty,
                NestedSetTypedValue<TKey>.Read(roots[relativeIndex], _store.Map.KeyProperty));
        }

        if (DetachActiveBatch() is { } errors)
        {
            // WHY: A failed detach callback is an insertion failure too. The executor must roll back the wave,
            // and retained entry handles let restoration retry every aggregate that could not be detached.
            throw new AggregateException("One or more imported entities could not be detached.", errors);
        }
    }
}
