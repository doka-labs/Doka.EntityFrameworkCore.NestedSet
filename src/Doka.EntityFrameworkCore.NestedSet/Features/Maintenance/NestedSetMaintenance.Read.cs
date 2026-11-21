using System.Runtime.ExceptionServices;
using AmbientTransaction = System.Transactions.Transaction;

namespace Doka.EntityFrameworkCore.NestedSet.Features.Maintenance;

internal sealed partial class NestedSetMaintenance<TEntity, TKey, TTreeId, TScope>
    where TEntity : class
    where TKey : notnull
    where TTreeId : notnull
    where TScope : notnull
{
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
            ordered,
            collectIssues,
            NestedSetIssueCollector<TKey>.ReportLimit,
            cancellationToken);
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
                operationCancellationToken => InspectAsync(collectRepairs, collectIssues, operationCancellationToken),
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
        var provider = NestedSetProviderCapabilities.Resolve(context);
        var transaction = context.Database.CurrentTransaction;
        var isolation = transaction?.GetDbTransaction()
            .IsolationLevel;

        var safeCallerSnapshot = provider.IsSafeReadSnapshot(isolation);
        var singleReadSnapshot = singleStatement && provider.HasStatementReadSnapshot(isolation);

        if (AmbientTransaction.Current is null)
        {
            if (safeCallerSnapshot || singleReadSnapshot)
            {
                // WHY: Native-key validation is one SELECT with an implicit statement snapshot on SQLite,
                // InnoDB, and PostgreSQL. Existing transaction snapshots also cover canonical-parent reads.
                return await read(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (transaction is null)
            {
                // WHY: InnoDB and PostgreSQL repeatable reads hold a stable MVCC snapshot across both SELECTs.
                // SQL Server SERIALIZABLE protects the read ranges without assuming database snapshot opt-in.
                // Read-only retries are safe: no hierarchy write or application tracker acceptance occurs here.
                var readIsolation = provider.ReadSnapshotIsolation;

                return await context
                    .Database
                    .CreateExecutionStrategy()
                    .ExecuteAsync(
                        operationCancellationToken => ReadSnapshotAsync(
                            read,
                            readIsolation,
                            operationCancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        TResult? result = default;
        await _executor
            .ExecuteReadAsync(
                async operationCancellationToken =>
                {
                    // WHY: SQL Server snapshot isolation requires database opt-in. Unsupported or weaker caller
                    // isolation keeps the established lock protocol rather than returning a mixed-version tree.
                    result = await read(operationCancellationToken)
                        .ConfigureAwait(false);
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
            result = await read(cancellationToken)
                .ConfigureAwait(false);
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
            ExceptionDispatchInfo
                .Capture(failure)
                .Throw();
        }

        return result!;
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
