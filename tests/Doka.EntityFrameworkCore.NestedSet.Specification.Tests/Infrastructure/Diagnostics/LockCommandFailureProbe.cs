namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Injects a failed infrastructure lock result without altering hierarchy commands.</summary>
internal sealed class LockCommandFailureProbe : DbCommandInterceptor
{
    private readonly DbException? _failure;

    /// <summary>Creates an observer that returns no affected rows or throws the supplied provider failure.</summary>
    /// <param name="failure">The exact database exception to propagate, or null to return zero affected rows.</param>
    internal LockCommandFailureProbe(
        DbException? failure = null
    )
    {
        _failure = failure;
    }

    /// <summary>Gets the number of infrastructure commands intercepted by this probe.</summary>
    internal int Attempts { get; private set; }

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
            Attempts++;

            if (_failure is not null)
            {
                throw _failure;
            }

            // WHY: Suppressing the provider command isolates the library's affected-row guard from provider errors.
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>Supplies a stable database failure instance for exception-preservation and privacy assertions.</summary>
internal sealed class LockProviderFailureException : DbException
{
    /// <summary>Creates a database failure whose sensitive message must never enter library telemetry.</summary>
    internal LockProviderFailureException() : base("private-provider-message-and-connection-details") { }
}
