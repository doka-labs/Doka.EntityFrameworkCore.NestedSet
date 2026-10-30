using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Fails on synchronous or asynchronous connection starts and on command creation.</summary>
internal sealed class CompositionIoBoundary : DbConnectionInterceptor
{
    /// <summary>Gets the number of attempted connection starts.</summary>
    internal int ConnectionStarts { get; private set; }

    /// <summary>Gets the fail-fast observer for commands, including commands on an already open connection.</summary>
    internal CommandBoundary Commands { get; } = new();

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result
    )
    {
        ConnectionStarts++;

        throw new InvalidOperationException("Query construction attempted to open a database connection.");
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

        throw new InvalidOperationException("Query construction attempted asynchronous database I/O.");
    }

    /// <summary>Counts command attempts before any provider command can execute.</summary>
    internal sealed class CommandBoundary : DbCommandInterceptor
    {
        /// <summary>Gets the number of command creation attempts.</summary>
        internal int Created { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult<DbCommand> CommandCreating(
            CommandCorrelatedEventData eventData,
            InterceptionResult<DbCommand> result
        )
        {
            Created++;

            throw new InvalidOperationException("Query construction attempted to create a database command.");
        }
    }
}
