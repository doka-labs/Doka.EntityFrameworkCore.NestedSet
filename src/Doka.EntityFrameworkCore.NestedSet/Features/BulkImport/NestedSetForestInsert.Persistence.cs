namespace Doka.EntityFrameworkCore.NestedSet.Features.BulkImport;

internal sealed partial class NestedSetForestInsert<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>Shares bounded payload waves across trees while preserving independent structural identities.</summary>
    /// <param name="prepared">The validated tree plans whose complete registry lock set is already held.</param>
    /// <param name="knownTracked">The immutable hierarchy identity baseline captured before import.</param>
    /// <param name="callbackWrites">The shared rollback log for every accepted application callback write.</param>
    /// <param name="cancellationToken">The token used for staging, persistence and structural finalization.</param>
    private async Task InsertPreparedAsync(
        PreparedTree[] prepared,
        HashSet<TEntity> knownTracked,
        NestedSetCallbackWrites callbackWrites,
        CancellationToken cancellationToken
    )
    {
        var order = NestedSetMapping<TEntity, TKey, TScope>.For(_context, _entityType)
            .Order;

        // WHY: Write each resolved destination back to the corresponding prepared-tree slot.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < prepared.Length; index++)
        {
            var tree = prepared[index];
            var destination = await tree
                .Insertion
                .PrepareDestinationAsync(tree.Plan, default, null, cancellationToken)
                .ConfigureAwait(false);

            prepared[index] = tree with { Destination = destination };

            if (order is null)
            {
                tree.Plan.PrepareGeometry(destination, null, cancellationToken);
            }
        }

        var active = new List<PreparedTree>(Math.Min(prepared.Length, NestedSetBatch.MaximumRows));
        var expected = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);
        var treeIndex = 0;
        var treeOffset = 0;
        var autoSavepoints = _context.Database.AutoSavepointsEnabled;

        try
        {
            // WHY: Every wave shares the executor's rollback boundary. Per-save EF savepoints would add commands
            // while all tree locks remain held, without making a partial forest independently recoverable.
            _context.Database.AutoSavepointsEnabled = false;

            while (treeIndex < prepared.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                active.Clear();
                expected.Clear();
                var remaining = NestedSetBatch.MaximumRows;

                while (remaining > 0
                    && treeIndex < prepared.Length)
                {
                    var tree = prepared[treeIndex];
                    var count = Math.Min(remaining, tree.Plan.Nodes.Count - treeOffset);
                    tree.Plan.StageBatch(tree.Destination, treeOffset, count, callbackWrites, cancellationToken);
                    active.Add(tree);

                    // WHY: Authorize exactly the staged slice of the current tree in the shared payload wave.
                    // ReSharper disable once ForCanBeConvertedToForeach
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    for (var index = treeOffset; index < treeOffset + count; index++)
                    {
                        expected.Add(tree.Plan.Nodes[index].Entity);
                    }

                    treeOffset += count;
                    remaining -= count;

                    if (treeOffset == tree.Plan.Nodes.Count)
                    {
                        treeIndex++;
                        treeOffset = 0;
                    }
                }

                // WHY: Independent one-node trees must share payload saves too. The wave guard authorizes the
                // exact union of staged roots while each plan still verifies its own typed tree coordinates.
                IReadOnlySet<IUpdateEntry> introducedEntries;
                IReadOnlySet<object> introducedEntities;

                using (var managedSave = NestedSetSaveChanges.EnterManagedSave(
                    _context,
                    active.SelectMany(tree => tree.Plan.ManagedEntities),
                    () => RequireSavedWave(active, knownTracked, expected),
                    () =>
                    {
                        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                        // ReSharper disable once ForCanBeConvertedToForeach
                        // ReSharper disable once LoopCanBeConvertedToQuery
                        for (int treeIndex2 = 0, treeCount = active.Count; treeIndex2 < treeCount; treeIndex2++)
                        {
                            var tree = active[treeIndex2];

                            tree.Plan.RefreshInsertionIdentities();
                        }
                    }))
                {
                    try
                    {
                        await _context
                            .SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        callbackWrites.Add(managedSave.CallbackWrites);
                    }

                    introducedEntries = managedSave.PersistedEntries;
                    introducedEntities = managedSave.PersistedEntities;
                }

                RequireSavedWave(active, knownTracked, expected);
                NestedSetTelemetry.RecordRowsAffected(NestedSetBatch.MaximumRows - remaining);
                NestedSetTelemetry.RecordBatch();
                callbackWrites.RecordSavedRelationships(
                    active.SelectMany(tree => tree.Plan.ActiveIdentities),
                    introducedEntries,
                    introducedEntities);

                // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (int treeIndex3 = 0, treeCount2 = active.Count; treeIndex3 < treeCount2; treeIndex3++)
                {
                    var tree = active[treeIndex3];

                    tree.Plan.CompleteBatch();
                }
            }
        }
        finally
        {
            _context.Database.AutoSavepointsEnabled = autoSavepoints;
        }

        foreach (var tree in prepared)
        {
            await tree
                .Insertion
                .FinalizePreparedAsync(tree.Plan, tree.Destination, null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Validates wave membership once and then verifies every participating plan's scalar structure.</summary>
    private void RequireSavedWave(
        IReadOnlyList<PreparedTree> active,
        HashSet<TEntity> knownTracked,
        HashSet<TEntity> expected
    )
    {
        NestedSetBulkPlan<TEntity, TKey, TTreeId, TScope>.RequireSavedMembership(
            _context,
            _entityType,
            knownTracked,
            expected);

        // WHY: Index this stable list directly without creating an enumerator or a copied buffer.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (int treeIndex4 = 0, treeCount3 = active.Count; treeIndex4 < treeCount3; treeIndex4++)
        {
            var tree = active[treeIndex4];

            tree.Plan.RequireSavedGeometry();
        }
    }
}
