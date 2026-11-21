namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
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

        // WHY: Validate staged roots against the matching absolute plan nodes.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
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

        // WHY: Match each saved batch root to the plan node receiving its generated identity.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
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
