namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

/// <summary>Coordinates consistent structural inspection and bounded, set-based repairs for one exact tree.</summary>
/// <typeparam name="TEntity">The mapped entity type.</typeparam>
/// <typeparam name="TKey">The mapped primary key type.</typeparam>
/// <typeparam name="TTreeId">The stable tree-identity type.</typeparam>
/// <typeparam name="TScope">The non-null scope property type.</typeparam>
internal sealed class NestedSetMaintenance<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
    /// <summary>The exact-tree queries and mapped structural selectors.</summary>
    private readonly NestedSetStore<TEntity, TKey, TTreeId, TScope> _store;

    /// <summary>The transaction and write-lock boundary shared with all other tree operations.</summary>
    private readonly NestedSetMutationExecutor<TEntity, TKey, TTreeId, TScope> _executor;

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
            CancellationToken token
        )
        {
            var result = await InspectAsync(true, false, token)
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
                token.ThrowIfCancellationRequested();
                await writer
                    .WriteAsync(result.Repairs, offset, token)
                    .ConfigureAwait(false);
            }

            await NestedSetTreeRegistryState
                .TouchAsync(_store.Context, _store.LockRequest(NestedSetTreeLockMode.Existing), token)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Reads structure and canonical parent keys before running the database-independent inspector.</summary>
    /// <param name="collectRepairs">Whether inspection should retain changed rows for repair.</param>
    /// <param name="collectIssues">Whether node-specific validation diagnostics should be retained.</param>
    /// <param name="cancellationToken">The token used for both reads and in-memory inspection.</param>
    /// <returns>The errors and optional repair rows for the locked snapshot.</returns>
    /// <remarks>The caller must hold a consistent read snapshot or the mutation executor's lock.</remarks>
    private async Task<NestedSetInspection<TKey>> InspectAsync(
        bool collectRepairs,
        bool collectIssues,
        CancellationToken cancellationToken
    )
    {
        var ordered = _store.Map.Order?.Mode == NestedSetOrderMode.Strict;
        var structure = ordered
            ? _store.Map.Order!
                .Apply(_store.Nodes)
                .Select(_store.Map.Projection)
            : _store.Structure;

        // WHY: Only strict rules replace stored positions. Flexible rebuild preserves explicit manual overrides.
        var nodes = await structure
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<TKey, TKey>? parents = null;
        if (!_store.Map.HasNativeKeyEquality)
        {
            parents = new Dictionary<TKey, TKey>(nodes.Length, _store.Map.KeyComparer);

            // WHY: String aliases must resolve with database collation. Unusual custom comparers and binary
            // keys retain the same canonical-link path rather than assuming CLR equality proves store equality.
            await foreach (var link in _store
                               .ParentLinks
                               .AsAsyncEnumerable()
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                parents.Add(link.Key, link.Parent);
            }
        }

        return NestedSetInspector<TKey>.Inspect(
            nodes,
            parents,
            _store.Map.KeyComparer,
            collectRepairs,
            cancellationToken,
            ordered,
            collectIssues);
    }

    /// <summary>Checks indexed local and aggregate invariants without materializing hierarchy rows.</summary>
    private async Task<NestedSetValidationReport> InspectQuickAsync(
        CancellationToken cancellationToken
    )
    {
        var map = _store.Map;
        var summary = await _store
            .Nodes
            .GroupBy(_ => 1)
            .Select(group => new NestedSetQuickSummary(
                group.LongCount(),
                group.LongCount(node => EF.Property<long>(node, map.Left) < 1
                    || EF.Property<long>(node, map.Right) <= EF.Property<long>(node, map.Left)),
                group.LongCount(node => EF.Property<int>(node, map.Depth) < 0),
                group.LongCount(node => EF.Property<long>(node, map.Position) < 0),
                group.Min(node => EF.Property<long>(node, map.Left)),
                group.Max(node => EF.Property<long>(node, map.Right))))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var rootCount = await _store
            .Siblings(default)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);

        var boundaryCount = summary.NodeCount == 0
            ? 0
            : await _store
                .Nodes
                .Select(node => EF.Property<long>(node, map.Left))
                .Concat(_store.Nodes.Select(node => EF.Property<long>(node, map.Right)))
                .Distinct()
                .LongCountAsync(cancellationToken)
                .ConfigureAwait(false);

        var issues = new List<NestedSetValidationIssue>(4);

        if (rootCount != 1)
        {
            issues.Add(
                new NestedSetValidationIssue(
                    NestedSetValidationCode.InvalidRootCount,
                    null,
                    "A TreeId must contain exactly one root."));
        }

        var expectedBoundaryCount = checked(summary.NodeCount * 2);
        if (summary.NodeCount > 0
            && (summary.InvalidBounds > 0
                || summary.MinimumLeft != 1
                || summary.MaximumRight != expectedBoundaryCount
                || boundaryCount != expectedBoundaryCount))
        {
            issues.Add(
                new NestedSetValidationIssue(
                    NestedSetValidationCode.InvalidBounds,
                    null,
                    "Bounds must form the complete unique interval from 1 through twice the node count."));
        }

        if (summary.InvalidDepths > 0)
        {
            issues.Add(
                new NestedSetValidationIssue(
                    NestedSetValidationCode.InvalidDepth,
                    null,
                    "Depth values cannot be negative."));
        }

        if (summary.InvalidPositions > 0)
        {
            issues.Add(
                new NestedSetValidationIssue(
                    NestedSetValidationCode.InvalidPosition,
                    null,
                    "Sibling positions cannot be negative."));
        }

        return new NestedSetValidationReport(NestedSetValidationLevel.Quick, summary.NodeCount, issues);
    }

    /// <summary>Uses a consistent read snapshot when available without acquiring the hierarchy write lock.</summary>
    private async Task<NestedSetInspection<TKey>> ReadAsync(
        bool collectRepairs,
        bool collectIssues,
        CancellationToken cancellationToken
    )
    {
        return await ReadConsistentAsync(
                token => InspectAsync(collectRepairs, collectIssues, token),
                _store.Map.HasNativeKeyEquality,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Runs one or more read statements against one stable provider snapshot.</summary>
    private async Task<TResult> ReadConsistentAsync<TResult>(
        Func<CancellationToken, Task<TResult>> read,
        bool singleStatement,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _store.Context;
        var kind = NestedSetProviderCapabilities.Resolve(context).Kind;
        var transaction = context.Database.CurrentTransaction;
        var isolation = transaction?.GetDbTransaction().IsolationLevel;
        var serverSnapshot = kind is NestedSetProviderKind.PostgreSql or NestedSetProviderKind.MySql;

        var safeCallerSnapshot = isolation is IsolationLevel.Serializable or IsolationLevel.Snapshot
            || (serverSnapshot && isolation == IsolationLevel.RepeatableRead);

        var singleReadSnapshot = singleStatement
            && ((serverSnapshot && (transaction is null || isolation == IsolationLevel.ReadCommitted))
                || (kind == NestedSetProviderKind.Sqlite && transaction is null));

        if (System.Transactions.Transaction.Current is null)
        {
            if (safeCallerSnapshot || singleReadSnapshot)
            {
                // WHY: Native-key validation is one SELECT with an implicit statement snapshot on SQLite,
                // InnoDB, and PostgreSQL. Existing transaction snapshots also cover canonical-parent reads.
                return await read(cancellationToken).ConfigureAwait(false);
            }

            if (transaction is null)
            {
                // WHY: InnoDB and PostgreSQL repeatable reads hold a stable MVCC snapshot across both SELECTs.
                // SQL Server SERIALIZABLE protects the read ranges without assuming database snapshot opt-in.
                // Read-only retries are safe: no hierarchy write or application tracker acceptance occurs here.
                var readIsolation = serverSnapshot ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable;

                return await context
                    .Database
                    .CreateExecutionStrategy()
                    .ExecuteAsync(token => ReadSnapshotAsync(read, readIsolation, token), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        TResult? result = default;
        await _executor
            .ExecuteReadAsync(
                async token =>
                {
                    // WHY: SQL Server snapshot isolation requires database opt-in. Unsupported or weaker caller
                    // isolation keeps the established lock protocol rather than returning a mixed-version tree.
                    result = await read(token).ConfigureAwait(false);
                },
                [_store.LockRequest(NestedSetTreeLockMode.Existing)],
                cancellationToken)
            .ConfigureAwait(false);

        return result!;
    }

    /// <summary>Owns a read transaction while preserving inspection and cleanup failures.</summary>
    private async Task<TResult> ReadSnapshotAsync<TResult>(
        Func<CancellationToken, Task<TResult>> read,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await _store
            .Context
            .Database
            .BeginTransactionAsync(isolation, cancellationToken)
            .ConfigureAwait(false);

        TResult? result = default;
        Exception? failure = null;
        try
        {
            result = await read(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception operationError)
        {
            failure = operationError;
        }

        try
        {
            // WHY: This transaction performs no writes and needs no commit. Disposal releases its read snapshot;
            // an uncanceled cleanup still runs after cancellation or a failed structural query.
            await snapshot
                .DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception disposalError)
        {
            failure = failure is null
                ? disposalError
                : new AggregateException(
                    "Nested-set read snapshot disposal failed. Discard the context.",
                    failure,
                    disposalError);
        }

        if (failure is not null)
        {
            System
                .Runtime
                .ExceptionServices
                .ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }

        return result!;
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

    /// <summary>Contains scalar results returned by the quick validation aggregate.</summary>
    private readonly record struct NestedSetQuickSummary(
        long NodeCount,
        long InvalidBounds,
        long InvalidDepths,
        long InvalidPositions,
        long MinimumLeft,
        long MaximumRight
    );
}
