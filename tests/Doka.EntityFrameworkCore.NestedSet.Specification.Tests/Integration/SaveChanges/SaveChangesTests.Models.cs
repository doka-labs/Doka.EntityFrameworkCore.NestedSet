namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses a typed public constructor so the same model can be leased through EF context pooling.</summary>
public sealed class SaveBoundaryContext : OrderingContext
{
    /// <summary>Creates a context with the fixture's real provider and test-specific observers.</summary>
    /// <param name="options">The immutable options also accepted by the pooled context factory.</param>
    public SaveBoundaryContext(
        DbContextOptions<SaveBoundaryContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override string TablePrefix => "Strict";
}

/// <summary>Observes native saves without changing SQL or retaining the context between pooled leases.</summary>
public sealed class SaveBoundaryProbe : DbCommandInterceptor
{
    /// <summary>Gets the number of native payload commands sent through this observer.</summary>
    public int PayloadCommands { get; private set; }

    /// <summary>Gets the number of observed commands targeting hierarchy lock infrastructure.</summary>
    public int LockCommands { get; private set; }

    /// <summary>Gets or sets whether the next payload command should simulate one transient failure.</summary>
    public bool FailNextPayload { get; set; }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Observe(command);

        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Observe(command);

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Observe(command);

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
        Observe(command);

        return ValueTask.FromResult(result);
    }

    /// <summary>Counts executed boundaries rather than depending on a provider's returning-clause convention.</summary>
    /// <param name="command">The command whose execution is about to begin.</param>
    private void Observe(
        DbCommand command
    )
    {
        if (command.CommandText.Contains("StrictOrderingLocks", StringComparison.Ordinal))
        {
            LockCommands++;
        }

        if (!command.CommandText.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
            && !command.CommandText.Contains("INSERT ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!command.CommandText.Contains("StrictOrderingMarkers", StringComparison.Ordinal)
            && !command.CommandText.Contains("StrictOrderingNodes", StringComparison.Ordinal))
        {
            return;
        }

        PayloadCommands++;

        if (FailNextPayload)
        {
            FailNextPayload = false;

            throw new SaveBoundaryTransientException();
        }
    }
}

/// <summary>Supplies a deterministic retry strategy that exercises EF's actual save retry boundary.</summary>
public sealed class SaveBoundaryRetryFactory : IExecutionStrategyFactory
{
    private readonly ExecutionStrategyDependencies _dependencies;

    /// <summary>Creates the scoped factory with EF's ordinary execution services.</summary>
    /// <param name="dependencies">The provider-independent services required by the execution strategy.</param>
    public SaveBoundaryRetryFactory(
        ExecutionStrategyDependencies dependencies
    )
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public IExecutionStrategy Create() => new SaveBoundaryRetry(_dependencies);

    /// <summary>Retries only the test's single injected failure and never hides unrelated database errors.</summary>
    private sealed class SaveBoundaryRetry : ExecutionStrategy
    {
        /// <summary>Creates a retry strategy with one immediate retry and no sleep-based coordination.</summary>
        /// <param name="dependencies">The real context and diagnostic services.</param>
        internal SaveBoundaryRetry(
            ExecutionStrategyDependencies dependencies
        ) : base(dependencies, 1, TimeSpan.Zero) { }

        /// <inheritdoc />
        protected override bool ShouldRetryOn(
            Exception exception
        ) => exception is SaveBoundaryTransientException;
    }
}

/// <summary>
///     Identifies the deterministic transient fault injected before a payload command reaches the server.
/// </summary>
public sealed class SaveBoundaryTransientException : Exception;

/// <summary>Changes the tracked graph at the same interceptor boundary used by application save callbacks.</summary>
public sealed class SaveBoundaryCallback : SaveChangesInterceptor
{
    /// <summary>Gets or sets the callback for one save; pooled reuse explicitly clears it between leases.</summary>
    public Action<DbContext>? OnSaving { get; set; }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        OnSaving?.Invoke(eventData.Context!);

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        OnSaving?.Invoke(eventData.Context!);

        return ValueTask.FromResult(result);
    }
}

/// <summary>
///     Exposes an inherited synchronous save that intentionally omits the documented custom-base wrapper.
/// </summary>
public sealed class UnwrappedSynchronousContext : DbContext
{
    /// <summary>Creates a negative control without supplying the synchronous save override.</summary>
    /// <param name="options">The established provider configuration with context-neutral option metadata.</param>
    public UnwrappedSynchronousContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => OrderingContext.ConfigureModel(modelBuilder, "Strict");
}
