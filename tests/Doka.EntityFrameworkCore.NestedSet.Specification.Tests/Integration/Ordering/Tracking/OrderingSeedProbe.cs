namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies bounded fixture setup and injects failures independently of measured refresh commands.</summary>
internal sealed class OrderingSeedProbe : DbCommandInterceptor
{
    private readonly int _failOnWrite;

    /// <summary>Creates a setup observer with an optional native-write failure boundary.</summary>
    /// <param name="failOnWrite">The one-based native attempt to reject, or zero for ordinary observation.</param>
    internal OrderingSeedProbe(
        int failOnWrite = 0
    ) => _failOnWrite = failOnWrite;

    /// <summary>Gets the exact injected failure so rollback tests reject unrelated setup failures.</summary>
    internal InvalidOperationException Failure { get; } = new("Injected ordering fixture seed failure.");

    /// <summary>Gets the maximum number of ordering entities tracked at an actual setup command boundary.</summary>
    internal int MaximumTrackedNodes { get; private set; }

    /// <summary>Gets the attempted native child writes, including an injected rejected attempt.</summary>
    internal int NativeWrites { get; private set; }

    /// <summary>Gets native writes that completed successfully before any later failure.</summary>
    internal int CompletedWrites { get; private set; }

    /// <summary>Gets the largest physical parameter count in a native setup write.</summary>
    internal int MaximumParameters { get; private set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        ObserveTracking(eventData.Context);

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        ObserveTracking(eventData.Context);

        if (IsNativeSeed(command))
        {
            NativeWrites++;
            MaximumParameters = Math.Max(MaximumParameters, command.Parameters.Count);

            if (NativeWrites == _failOnWrite)
            {
                throw Failure;
            }
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (IsNativeSeed(command))
        {
            // WHY: Affected-row reporting differs by provider and SQL Server NOCOUNT;
            // verify stored rows separately.
            CompletedWrites++;
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    ///     Counts only the actual ordering model's tracked entries, including root saves before native writes.
    /// </summary>
    private void ObserveTracking(
        DbContext? context
    )
    {
        if (context is not null)
        {
            var tracked = context
                .ChangeTracker
                .Entries<OrderingNode>()
                .Count();

            MaximumTrackedNodes = Math.Max(MaximumTrackedNodes, tracked);
        }
    }

    /// <summary>
    ///     Scopes fault injection to child seeding so registry setup and verification remain independent.
    /// </summary>
    private static bool IsNativeSeed(
        DbCommand command
    ) => command.CommandText.StartsWith(OrderingRefreshTestSupport.SeedCommandTag, StringComparison.Ordinal);
}
