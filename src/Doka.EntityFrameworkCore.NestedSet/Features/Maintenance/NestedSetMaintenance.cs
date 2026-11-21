namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Coordinates consistent structural inspection and bounded, set-based repairs for one exact tree.</summary>
/// <typeparam name="TEntity">The mapped entity type.</typeparam>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The non-null scope property type.</typeparam>
internal sealed partial class NestedSetMaintenance<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>The transaction and write-lock boundary shared with all other tree operations.</summary>
    private readonly NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> _executor;

    /// <summary>The exact-tree queries and mapped structural selectors.</summary>
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;

    /// <summary>Creates maintenance operations sharing the exact-tree store and mutation boundary.</summary>
    /// <param name="store">The tree-bound store that projects mapped structural values.</param>
    /// <param name="executor">The executor that serializes reads and repairs against other writers.</param>
    internal NestedSetMaintenance(
        NestedSetStore<TEntity, TKey, TTreeId, TScope> store,
        NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> executor
    )
    {
        _store = store;
        _executor = executor;
    }

    /// <summary>Builds a bounded validation report without exposing the mapped key type.</summary>
    internal async Task<NestedSetValidationReport> ValidateAsync(
        NestedSetValidationLevel level,
        CancellationToken cancellationToken
    )
    {
        if (level is not NestedSetValidationLevel.Quick and not NestedSetValidationLevel.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "The validation level is unsupported.");
        }

        if (level == NestedSetValidationLevel.Quick)
        {
            return await ReadConsistentAsync(InspectQuickAsync, false, cancellationToken)
                .ConfigureAwait(false);
        }

        var result = await ReadAsync(false, true, cancellationToken)
            .ConfigureAwait(false);

        return new NestedSetValidationReport(
            level,
            result.NodeCount,
            ToPublicIssues(result),
            result.IssueCounts,
            result.TotalIssueCount);
    }

    /// <summary>Computes the exact coordinate repair set without changing the selected tree.</summary>
    internal async Task<NestedSetRebuildPlan> PlanRebuildAsync(
        CancellationToken cancellationToken
    )
    {
        var result = await ReadAsync(true, true, cancellationToken)
            .ConfigureAwait(false);

        NestedSetTelemetry.RecordRebuildNodes(result.NodeCount);
        IReadOnlyList<NestedSetStructuralRole> roles = result.Repairs.Count == 0
            ? []
            :
            [
                NestedSetStructuralRole.Left,
                NestedSetStructuralRole.Right,
                NestedSetStructuralRole.Depth,
                NestedSetStructuralRole.Position,
            ];

        var batchCount = checked((int)(((long)result.Repairs.Count + NestedSetBatch.MaximumRows - 1)
            / NestedSetBatch.MaximumRows));

        return new NestedSetRebuildPlan(
            result.CanRebuild,
            result.NodeCount,
            result.Repairs.Count,
            batchCount,
            roles,
            ToPublicIssues(result),
            result.IssueCounts,
            result.TotalIssueCount);
    }

    /// <summary>Repairs changed intervals, depths and dense positions after validating all stored adjacency.</summary>
    /// <param name="cancellationToken">The token used to cancel locking, inspection, and repair batches.</param>
    /// <returns>A task that completes when the executor has finished the atomic operation.</returns>
    /// <exception cref="InvalidOperationException">Stored adjacency or sibling ordering is ambiguous.</exception>
    internal Task RebuildAsync(
        CancellationToken cancellationToken
    )
    {
        return _executor.ExecuteAsync(
            RebuildCoreAsync,
            [_store.LockRequest(NestedSetTreeLockMode.Existing)],
            cancellationToken);

        async Task RebuildCoreAsync(
            CancellationToken operationCancellationToken
        )
        {
            var result = await InspectAsync(true, false, operationCancellationToken)
                .ConfigureAwait(false);

            NestedSetTelemetry.RecordRebuildNodes(result.NodeCount);

            if (!result.CanRebuild)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidStructure,
                    "Cannot rebuild: " + string.Join(" ", result.Errors));
            }

            var writer = new NestedSetRepairWriter<TEntity, TKey, TTreeId, TScope>(_store);

            // WHY: Every parent link is validated before the first update; executor rollback covers every batch.
            for (var offset = 0; offset < result.Repairs.Count; offset += NestedSetBatch.MaximumRows)
            {
                operationCancellationToken.ThrowIfCancellationRequested();
                await writer
                    .WriteAsync(result.Repairs, offset, operationCancellationToken)
                    .ConfigureAwait(false);
            }

            await NestedSetTreeRegistryState
                .TouchAsync(
                    _store.Context,
                    _store.LockRequest(NestedSetTreeLockMode.Existing),
                    operationCancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Erases the mapped key type while preserving stable codes and exact key values.</summary>
    private static List<NestedSetValidationIssue> ToPublicIssues(
        NestedSetInspection<TKey> inspection
    )
    {
        var issues = inspection
            .Issues
            .Select(issue => new NestedSetValidationIssue(issue.Code, issue.NodeKey, issue.Message))
            .ToList();

        issues.AddRange(inspection.TreeIssues);

        return issues;
    }
}
