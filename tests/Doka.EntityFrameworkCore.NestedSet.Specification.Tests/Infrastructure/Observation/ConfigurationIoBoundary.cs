namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Observes attempted database access while adapter or supplied-model configuration is being rejected.</summary>
internal sealed class ConfigurationIoBoundary : DbConnectionInterceptor
{
    private readonly CommandBoundary _commands = new();

    /// <summary>Gets the number of synchronous and asynchronous connection-opening attempts.</summary>
    internal int ConnectionStarts { get; private set; }

    /// <summary>Gets the command interceptor installed alongside this connection observer.</summary>
    internal DbCommandInterceptor Commands => _commands;

    /// <summary>Gets the number of database commands requested by the observed context.</summary>
    internal int CommandsCreated => _commands.Created;

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result
    )
    {
        ConnectionStarts++;

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        ConnectionStarts++;

        return ValueTask.FromResult(result);
    }

    /// <summary>Counts command creation before an executor could hide an attempted database operation.</summary>
    private sealed class CommandBoundary : DbCommandInterceptor
    {
        /// <summary>Gets the number of observed command-creation attempts.</summary>
        internal int Created { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult<DbCommand> CommandCreating(
            CommandCorrelatedEventData eventData,
            InterceptionResult<DbCommand> result
        )
        {
            Created++;

            return result;
        }
    }
}
