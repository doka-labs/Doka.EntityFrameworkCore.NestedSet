using System.Data.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Diagnostics;

/// <summary>Measures library-owned boundaries without recording any application data.</summary>
internal static class NestedSetTelemetry
{
    /// <summary>Flows operation-local counters across asynchronous database continuations.</summary>
    private static readonly AsyncLocal<OperationState?> s_current = new();

    /// <summary>The process-wide source whose lifetime is owned by the library.</summary>
    private static readonly ActivitySource s_source = new(NestedSetDiagnostics.ActivitySourceName);

    /// <summary>The shared meter also supports services constructed without dependency injection.</summary>
    private static readonly Meter s_meter = new(NestedSetDiagnostics.MeterName);

    /// <summary>Recommends duration buckets from submillisecond work through prolonged lock contention.</summary>
    private static readonly InstrumentAdvice<double> s_durationAdvice = new()
    {
        // WHY: Generic collector defaults group positive subsecond durations together; these boundaries use seconds.
        // A shared, fixed-size recommendation limits aggregation memory while collectors retain override control.
        HistogramBucketBoundaries =
            [0.0001, 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60],
    };

    /// <summary>The wall-clock duration of a complete public operation, including transaction cleanup.</summary>
    private static readonly Histogram<double> s_duration = s_meter.CreateHistogram<double>(
        "nestedset.operation.duration",
        "s",
        "Duration of a complete hierarchy operation.",
        advice: s_durationAdvice);

    /// <summary>The completed operation count, partitioned by bounded operation and outcome tags.</summary>
    private static readonly Counter<long> s_operations = s_meter.CreateCounter<long>(
        "nestedset.operation.count",
        "{operation}",
        "Completed hierarchy operations.");

    /// <summary>The number of failed operations; cancellation remains a separate outcome.</summary>
    private static readonly Counter<long> s_failures = s_meter.CreateCounter<long>(
        "nestedset.operation.failures",
        "{failure}",
        "Failed hierarchy operations, excluding cancellation.");

    /// <summary>The wall-clock duration of lock acquisition, excluding the operation protected by that lock.</summary>
    private static readonly Histogram<double> s_lockWait = s_meter.CreateHistogram<double>(
        "nestedset.lock.wait.duration",
        "s",
        "Time spent acquiring a hierarchy write lock.",
        advice: s_durationAdvice);

    /// <summary>The sum of hierarchy rows reported by successful database write commands.</summary>
    private static readonly Histogram<long> s_rowsAffected = s_meter.CreateHistogram<long>(
        "nestedset.rows.affected",
        "{row}",
        "Hierarchy rows affected by one completed operation.");

    /// <summary>The number of bounded write batches executed by one bulk or rebuild operation.</summary>
    private static readonly Histogram<long> s_batchCount = s_meter.CreateHistogram<long>(
        "nestedset.batch.count",
        "{batch}",
        "Bounded hierarchy write batches executed by one operation.");

    /// <summary>The number of nodes inspected by one rebuild operation.</summary>
    private static readonly Histogram<long> s_rebuildNodeCount = s_meter.CreateHistogram<long>(
        "nestedset.rebuild.node.count",
        "{node}",
        "Hierarchy nodes inspected by one rebuild operation.");

    // WHY: Parent moves managed by SaveChanges are implementation steps of its public operation. Keeping the
    // outer state active attributes their row counts to the save without hiding unrelated nested operations.
    /// <summary>Observes one command without allocating a tracing state machine when nobody subscribes.</summary>
    /// <param name="context">The context used only to classify its verified provider family.</param>
    /// <param name="operation">A library-owned operation name; never derived from application data.</param>
    /// <param name="action">The complete operation, including commit or rollback.</param>
    /// <param name="cancellationToken">The token forwarded unchanged to the operation.</param>
    /// <returns>The operation's completion task.</returns>
    internal static Task ExecuteAsync(
        DbContext context,
        string operation,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken
    ) => IsOperationEnabled() && !(s_current.Value is not null && NestedSetSaveChanges.IsManagedMutation(context))
        ? ExecuteObservedAsync(ProviderName(context), operation, action, cancellationToken)
        : action(cancellationToken);

    /// <summary>Observes one result-producing operation using the same bounded classifications as mutations.</summary>
    /// <typeparam name="TResult">The result type, which is never inspected or recorded.</typeparam>
    /// <param name="context">The context used only to classify its verified provider family.</param>
    /// <param name="operation">A library-owned operation name.</param>
    /// <param name="action">The complete result-producing operation.</param>
    /// <param name="cancellationToken">The token forwarded unchanged to the operation.</param>
    /// <returns>The original operation result.</returns>
    internal static Task<TResult> ExecuteAsync<TResult>(
        DbContext context,
        string operation,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken
    ) => IsOperationEnabled() && !(s_current.Value is not null && NestedSetSaveChanges.IsManagedMutation(context))
        ? ExecuteObservedAsync(ProviderName(context), operation, action, cancellationToken)
        : action(cancellationToken);

    /// <summary>Records a provider-reported hierarchy row count without wrapping disabled operations.</summary>
    /// <param name="operation">The database task whose result is the affected hierarchy row count.</param>
    /// <returns>The original result after adding it to the active operation.</returns>
    internal static Task<int> TrackRows(
        Task<int> operation
    )
    {
        var state = s_current.Value;

        return state is null ? operation : TrackRowsAsync(operation, state);
    }

    /// <summary>Adds a known hierarchy row count to the active operation.</summary>
    /// <param name="count">The non-negative number of hierarchy rows written.</param>
    internal static void RecordRowsAffected(
        long count
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        s_current.Value?.AddRows(count);
    }

    /// <summary>Adds one bounded write batch to the active operation.</summary>
    internal static void RecordBatch() => s_current.Value?.AddBatch();

    /// <summary>Records the complete node count inspected by the active rebuild operation.</summary>
    /// <param name="count">The non-negative number of inspected nodes.</param>
    internal static void RecordRebuildNodes(
        long count
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        s_current.Value?.SetRebuildNodes(count);
    }

    /// <summary>Measures waiting for a real database lock without counting it as another public operation.</summary>
    /// <param name="acquire">The provider-specific lock acquisition.</param>
    /// <param name="cancellationToken">The cancellation token forwarded to acquisition.</param>
    /// <returns>A task that completes when the lock is held or acquisition fails.</returns>
    internal static Task MeasureLockAsync(
        Func<CancellationToken, Task> acquire,
        CancellationToken cancellationToken
    ) => s_source.HasListeners() || s_lockWait.Enabled
        ? MeasureLockObservedAsync(acquire, cancellationToken)
        : acquire(cancellationToken);

    /// <summary>Measures providers that acquire their writer reservation while beginning a transaction.</summary>
    /// <typeparam name="TResult">The acquired resource returned unchanged to its owner.</typeparam>
    /// <param name="acquire">The provider's actual resource acquisition boundary.</param>
    /// <param name="cancellationToken">The token forwarded unchanged to acquisition.</param>
    /// <returns>The acquired resource, whose disposal remains the caller's responsibility.</returns>
    internal static Task<TResult> MeasureLockAsync<TResult>(
        Func<CancellationToken, Task<TResult>> acquire,
        CancellationToken cancellationToken
    ) => s_source.HasListeners() || s_lockWait.Enabled
        ? MeasureLockObservedAsync(acquire, cancellationToken)
        : acquire(cancellationToken);

    /// <summary>Checks all operation instruments so a metrics-only subscriber receives failures and counts.</summary>
    /// <returns>Whether any consumer observes an operation instrument.</returns>
    private static bool IsOperationEnabled() => s_source.HasListeners()
        || s_duration.Enabled
        || s_operations.Enabled
        || s_failures.Enabled
        || s_rowsAffected.Enabled
        || s_batchCount.Enabled
        || s_rebuildNodeCount.Enabled;

    /// <summary>Completes tracing after the command has performed all success or failure cleanup.</summary>
    /// <param name="provider">The bounded provider family.</param>
    /// <param name="operation">The bounded operation name.</param>
    /// <param name="action">The operation to observe.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task preserving the original operation result or exception.</returns>
    private static async Task ExecuteObservedAsync(
        string provider,
        string operation,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken
    )
    {
        using var activity = s_source.StartActivity("nestedset." + operation, ActivityKind.Internal);
        var started = Stopwatch.GetTimestamp();
        var previous = s_current.Value;
        var state = new OperationState(provider);
        s_current.Value = state;
        Exception? failure = null;

        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;

            throw;
        }
        finally
        {
            s_current.Value = previous;
            Complete(operation, started, activity, failure, state);
        }
    }

    /// <summary>Completes tracing for a result without retaining the result in instrumentation state.</summary>
    /// <typeparam name="TResult">The unobserved result type.</typeparam>
    /// <param name="provider">The bounded provider family.</param>
    /// <param name="operation">The bounded operation name.</param>
    /// <param name="action">The operation to observe.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The unmodified result.</returns>
    private static async Task<TResult> ExecuteObservedAsync<TResult>(
        string provider,
        string operation,
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken
    )
    {
        using var activity = s_source.StartActivity("nestedset." + operation, ActivityKind.Internal);
        var started = Stopwatch.GetTimestamp();
        var previous = s_current.Value;
        var state = new OperationState(provider);
        s_current.Value = state;
        Exception? failure = null;

        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;

            throw;
        }
        finally
        {
            s_current.Value = previous;
            Complete(operation, started, activity, failure, state);
        }
    }

    /// <summary>Captures lock latency, including canceled and failed attempts.</summary>
    /// <param name="acquire">The real provider lock acquisition.</param>
    /// <param name="cancellationToken">The cancellation token forwarded unchanged.</param>
    /// <returns>A task preserving the provider's completion or failure.</returns>
    private static async Task MeasureLockObservedAsync(
        Func<CancellationToken, Task> acquire,
        CancellationToken cancellationToken
    )
    {
        using var activity = s_source.StartActivity("nestedset.lock", ActivityKind.Internal);
        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;

        try
        {
            await acquire(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;

            throw;
        }
        finally
        {
            var tags = CreateTags("lock", failure, s_current.Value?.Provider ?? "unknown");
            CompleteActivity(activity, tags, failure);
            s_lockWait.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }

    /// <summary>Observes a transaction's writer reservation without owning the returned transaction.</summary>
    /// <typeparam name="TResult">The acquired resource type.</typeparam>
    /// <param name="acquire">The provider's actual resource acquisition boundary.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The original acquired resource.</returns>
    private static async Task<TResult> MeasureLockObservedAsync<TResult>(
        Func<CancellationToken, Task<TResult>> acquire,
        CancellationToken cancellationToken
    )
    {
        using var activity = s_source.StartActivity("nestedset.lock", ActivityKind.Internal);
        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;

        try
        {
            return await acquire(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;

            throw;
        }
        finally
        {
            var tags = CreateTags("lock", failure, s_current.Value?.Provider ?? "unknown");
            CompleteActivity(activity, tags, failure);
            s_lockWait.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }

    /// <summary>Records completed operation metrics using only constant names and classifications.</summary>
    /// <param name="operation">The bounded operation name.</param>
    /// <param name="started">The monotonic timestamp captured before invocation.</param>
    /// <param name="activity">The sampled activity, if tracing is enabled.</param>
    /// <param name="failure">The exception used for classification only.</param>
    /// <param name="state">The operation-local structural measurements.</param>
    private static void Complete(
        string operation,
        long started,
        Activity? activity,
        Exception? failure,
        OperationState state
    )
    {
        var tags = CreateTags(operation, failure, state.Provider);
        CompleteActivity(activity, tags, failure);
        s_duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        s_operations.Add(1, tags);

        if (state.HasRows)
        {
            s_rowsAffected.Record(state.RowsAffected, tags);
        }

        if (state.BatchCount > 0)
        {
            s_batchCount.Record(state.BatchCount, tags);
        }

        if (state.RebuildNodeCount is { } rebuildNodeCount)
        {
            s_rebuildNodeCount.Record(rebuildNodeCount, tags);
        }

        if (failure is not null and not OperationCanceledException)
        {
            s_failures.Add(1, tags);
        }
    }

    /// <summary>Creates bounded tags without exception text, CLR type names, or application identifiers.</summary>
    /// <param name="operation">The library-owned operation name.</param>
    /// <param name="failure">The exception to classify without exposing its data.</param>
    /// <param name="provider">The bounded provider family.</param>
    /// <returns>A small inline tag list shared by tracing and metrics.</returns>
    private static TagList CreateTags(
        string operation,
        Exception? failure,
        string provider
    )
    {
        var outcome = failure switch
        {
            null => "success",
            OperationCanceledException => "canceled",
            _ => "failure",
        };

        var tags = new TagList
        {
            { "nestedset.operation", operation },
            { "nestedset.outcome", outcome },
            { "nestedset.provider", provider },
        };

        if (failure is not null)
        {
            // WHY: Exception messages and provider type names can reveal payloads or create unbounded cardinality.
            tags.Add("error.type", Classify(failure));
        }

        return tags;
    }

    /// <summary>Maps one verified provider to a bounded, non-identifying diagnostic family.</summary>
    private static string ProviderName(
        DbContext context
    ) => NestedSetProviderCapabilities.Resolve(context).Kind switch
    {
        NestedSetProviderKind.Sqlite => "sqlite",
        NestedSetProviderKind.MySql => "mysql_mariadb",
        NestedSetProviderKind.PostgreSql => "postgresql",
        NestedSetProviderKind.SqlServer => "sql_server",
        _ => "unknown",
    };

    /// <summary>Records one completed database task against the state captured before its await.</summary>
    private static async Task<int> TrackRowsAsync(
        Task<int> operation,
        OperationState state
    )
    {
        var count = await operation.ConfigureAwait(false);
        state.AddRows(count);

        return count;
    }

    /// <summary>Maps known failures to a finite diagnostic vocabulary.</summary>
    /// <param name="failure">The failure whose payload is never accessed.</param>
    /// <returns>A bounded error category.</returns>
    private static string Classify(
        Exception failure
    ) => failure switch
    {
        NestedSetException { Code: NestedSetErrorCode.NodeNotFound } => "node_not_found",
        NestedSetException { Code: NestedSetErrorCode.CycleDetected } => "cycle_detected",
        NestedSetException { Code: NestedSetErrorCode.InvalidStructure } => "invalid_structure",
        NestedSetException { Code: NestedSetErrorCode.InvalidContext } => "invalid_context",
        NestedSetException { Code: NestedSetErrorCode.InvalidTransaction } => "invalid_transaction",
        NestedSetException { Code: NestedSetErrorCode.ManualPlacementNotAllowed } => "manual_placement_not_allowed",
        NestedSetException { Code: NestedSetErrorCode.LockAcquisitionFailed } => "lock_acquisition_failed",
        NestedSetException { Code: NestedSetErrorCode.InvalidImport } => "invalid_import",
        NestedSetException { Code: NestedSetErrorCode.TreeNotFound } => "tree_not_found",
        NestedSetException { Code: NestedSetErrorCode.TreeIdUnavailable } => "tree_id_unavailable",
        NestedSetException { Code: NestedSetErrorCode.TreeIdNotTombstoned } => "tree_id_not_tombstoned",
        NestedSetException { Code: NestedSetErrorCode.ConcurrentTreeIdentity } => "concurrent_tree_identity",
        NestedSetException => "operation_rejected",
        OperationCanceledException => "canceled",
        ArgumentException => "invalid_argument",
        OverflowException => "overflow",
        NotSupportedException => "unsupported",
        DbUpdateException or DbException => "database",
        _ => "unexpected",
    };

    /// <summary>Sets sampled activity metadata without recording exceptions or status descriptions.</summary>
    /// <param name="activity">The optional sampled activity.</param>
    /// <param name="tags">The bounded tags already prepared for metrics.</param>
    /// <param name="failure">Whether the operation failed or was canceled.</param>
    private static void CompleteActivity(
        Activity? activity,
        TagList tags,
        Exception? failure
    )
    {
        if (activity is null)
        {
            return;
        }

        foreach (var tag in tags)
        {
            activity.SetTag(tag.Key, tag.Value);
        }

        activity.SetStatus(failure is null ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    /// <summary>Retains low-cardinality measurements for exactly one observed public operation.</summary>
    private sealed class OperationState
    {
        private long _rowsAffected;
        private long _batchCount;
        private long _rebuildNodeCount = -1;

        /// <summary>Creates counters for one bounded provider family.</summary>
        internal OperationState(
            string provider
        )
        {
            Provider = provider;
        }

        /// <summary>Gets the bounded provider family.</summary>
        internal string Provider { get; }

        /// <summary>Gets whether at least one hierarchy write command reported a row count.</summary>
        internal bool HasRows { get; private set; }

        /// <summary>Gets the sum of hierarchy rows reported across write commands.</summary>
        internal long RowsAffected => Interlocked.Read(ref _rowsAffected);

        /// <summary>Gets the number of bounded write batches.</summary>
        internal long BatchCount => Interlocked.Read(ref _batchCount);

        /// <summary>Gets the rebuild node count when the operation is a rebuild.</summary>
        internal long? RebuildNodeCount
        {
            get
            {
                var value = Interlocked.Read(ref _rebuildNodeCount);

                return value < 0 ? null : value;
            }
        }

        /// <summary>Adds a provider-reported hierarchy row count.</summary>
        internal void AddRows(
            long count
        )
        {
            HasRows = true;
            _ = Interlocked.Add(ref _rowsAffected, count);
        }

        /// <summary>Adds one bounded write batch.</summary>
        internal void AddBatch() => _ = Interlocked.Increment(ref _batchCount);

        /// <summary>Sets the complete inspected rebuild node count.</summary>
        internal void SetRebuildNodes(
            long count
        ) => Interlocked.Exchange(ref _rebuildNodeCount, count);
    }
}
