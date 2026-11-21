namespace Doka.EntityFrameworkCore.NestedSet.Diagnostics;

/// <summary>Measures library-owned boundaries without recording any application data.</summary>
internal static partial class NestedSetTelemetry
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
    private static readonly Histogram<double> s_duration = s_meter.CreateHistogram(
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
    private static readonly Histogram<double> s_lockWait = s_meter.CreateHistogram(
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
    /// <remarks>Completion finalization cannot be canceled independently of the already-started command.</remarks>
    internal static Task<int> TrackRows(
        Task<int> operation
    )
    {
        var state = s_current.Value;

        // WHY: The command already owns cancellation. Finalization must observe its terminal state before
        // transaction cleanup; a second cancelable wait could abandon a command that is still writing.
        return state is null
            ? operation
            : TrackRowsAsync(operation, state);
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
}
