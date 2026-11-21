namespace Doka.EntityFrameworkCore.NestedSet.Tests;

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
