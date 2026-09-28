namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Injects cleanup failures without replacing the earlier operation failure.</summary>
public sealed class EnterpriseTransactionProbe : DbTransactionInterceptor
{
    /// <summary>Gets or sets whether the rollback of a service-owned transaction fails.</summary>
    public bool FailOwnedRollback { get; set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (FailOwnedRollback)
        {
            throw new InjectedCleanupException();
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => throw new InjectedCleanupException();
}

/// <summary>Identifies an injected rollback failure independently of the original operation failure.</summary>
public sealed class InjectedCleanupException : Exception;

/// <summary>Signals a tree-registry lock attempt without waiting for its database result.</summary>
public sealed class RegistryLockProbe : DbCommandInterceptor
{
    /// <summary>Gets the signal completed immediately before a registry lock command is sent to the database.</summary>
    public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (NestedSetTestInfrastructure.ReferencesRegistry(eventData.Context, command.CommandText))
        {
            Attempted.TrySetResult();
        }

        return ValueTask.FromResult(result);
    }
}
