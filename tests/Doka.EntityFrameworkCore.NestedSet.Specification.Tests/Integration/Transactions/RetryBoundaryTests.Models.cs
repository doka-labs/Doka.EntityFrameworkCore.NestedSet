namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Owns intentionally substituted retry services outside EF's process-global provider cache.</summary>
internal static class RetryBoundaryServices
{
    /// <summary>Owns the intentionally replaced execution strategy for one retry-boundary experiment.</summary>
    /// <param name="engine">The real provider whose ordinary relational services remain active.</param>
    /// <returns>The service graph disposed after the test's context.</returns>
    internal static ServiceProvider Create(
        string engine
    )
    {
        var services = new ServiceCollection();

        switch (engine)
        {
            case "Sqlite":
                services.AddEntityFrameworkSqlite();
                break;
            case "MySql":
            case "MariaDb":
                services.AddEntityFrameworkDokaMySql();
                break;
            case "PostgreSql":
                services.AddEntityFrameworkNpgsql();
                break;
            case "SqlServer":
                services.AddEntityFrameworkSqlServer();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(engine));
        }

        // WHY: This experiment replaces a provider service deliberately. Explicit ownership prevents its test-only
        // graph from accumulating in EF's process-global cache while leaving ordinary provider caching observable.
        services.AddScoped<IExecutionStrategyFactory, SaveBoundaryRetryFactory>();

        // WHY: UseInternalServiceProvider makes the caller responsible for every options-extension service. Apply
        // the production extension's registrations so this retry graph builds the same registry-enabled model.
        ((IDbContextOptionsExtension)NestedSetOptionsExtension.Instance).ApplyServices(services);

        return services.BuildServiceProvider();
    }
}

/// <summary>Verifies rejected hierarchy work does not begin an owned database transaction.</summary>
internal sealed class RetryBoundaryTransactionProbe : DbTransactionInterceptor
{
    /// <summary>Gets the number of transaction starts attempted through this context.</summary>
    internal int Starts { get; private set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default
    )
    {
        Starts++;

        return ValueTask.FromResult(result);
    }
}

/// <summary>Exercises EF's active-strategy marker without enabling replay.</summary>
internal sealed class RetryBoundaryNonRetryStrategy : ExecutionStrategy
{
    /// <summary>Creates a genuine EF execution boundary with zero retry attempts.</summary>
    internal RetryBoundaryNonRetryStrategy(
        DbContext context
    ) : base(context, 0, TimeSpan.Zero) { }

    /// <inheritdoc />
    protected override bool ShouldRetryOn(
        Exception exception
    ) => false;
}
