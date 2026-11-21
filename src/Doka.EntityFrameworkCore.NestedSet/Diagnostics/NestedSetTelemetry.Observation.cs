using System.Data.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Diagnostics;

internal static partial class NestedSetTelemetry
{
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
        using var activity = s_source.StartActivity("nestedset." + operation);
        var started = Stopwatch.GetTimestamp();
        var previous = s_current.Value;
        var state = new OperationState(provider);
        s_current.Value = state;
        Exception? failure = null;

        try
        {
            await action(cancellationToken)
                .ConfigureAwait(false);
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
        using var activity = s_source.StartActivity("nestedset." + operation);
        var started = Stopwatch.GetTimestamp();
        var previous = s_current.Value;
        var state = new OperationState(provider);
        s_current.Value = state;
        Exception? failure = null;

        try
        {
            return await action(cancellationToken)
                .ConfigureAwait(false);
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
        using var activity = s_source.StartActivity("nestedset.lock");
        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;

        try
        {
            await acquire(cancellationToken)
                .ConfigureAwait(false);
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
            s_lockWait.Record(
                Stopwatch.GetElapsedTime(started)
                    .TotalSeconds,
                tags);
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
        using var activity = s_source.StartActivity("nestedset.lock");
        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;

        try
        {
            return await acquire(cancellationToken)
                .ConfigureAwait(false);
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
            s_lockWait.Record(
                Stopwatch.GetElapsedTime(started)
                    .TotalSeconds,
                tags);
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
        s_duration.Record(
            Stopwatch.GetElapsedTime(started)
                .TotalSeconds,
            tags);

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
    ) => NestedSetProviderCapabilities.Resolve(context)
        .TelemetryName;

    /// <summary>Records one completed database task against the state captured before its await.</summary>
    private static async Task<int> TrackRowsAsync(
        Task<int> operation,
        OperationState state
    )
    {
        // WHY: Finish command observation and row accounting together; caller cancellation belongs to
        // the provider task, never to this mandatory completion path preceding transaction cleanup.
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

        activity.SetStatus(
            failure is null
                ? ActivityStatusCode.Ok
                : ActivityStatusCode.Error);
    }
}
