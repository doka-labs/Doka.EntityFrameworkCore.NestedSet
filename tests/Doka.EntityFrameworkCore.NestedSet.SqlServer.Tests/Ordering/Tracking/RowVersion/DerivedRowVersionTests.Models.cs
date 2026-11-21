using System.Collections.Generic;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

public sealed partial class DerivedRowVersionTests
{
    /// <summary>Observes only token refresh reads after payload SQL, with an optional late failure.</summary>
    private sealed class RefreshProbe : DbCommandInterceptor
    {
        /// <summary>Gets or sets whether the enclosing test has completed its payload save.</summary>
        internal bool Active { get; set; }

        /// <summary>Gets or sets whether a second token refresh should fail after the first one completed.</summary>
        internal bool FailAfterFirstRead { get; init; }

        /// <summary>Gets or sets whether the atomic import's tagged final refresh should fail.</summary>
        internal bool FailBulkRefresh { get; init; }

        /// <summary>Gets whether the import reached its final refresh after assigning generated input values.</summary>
        internal bool BulkRefreshAttempted { get; private set; }

        /// <summary>Gets scalar refresh commands without recording seed or application payload reads.</summary>
        internal List<string> Commands { get; } = [];

        /// <inheritdoc />
        public override ValueTask<
                InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default
            )
        {
            if (FailBulkRefresh
                && command.CommandText.Contains(
                    "Doka.EntityFrameworkCore.NestedSet.BulkRefresh",
                    StringComparison.Ordinal))
            {
                BulkRefreshAttempted = true;

                throw new InvalidOperationException("Derived token bulk refresh probe.");
            }

            if (Active
                && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("[Version]", StringComparison.Ordinal))
            {
                Commands.Add(command.CommandText);

                if (FailAfterFirstRead && Commands.Count == 2)
                {
                    throw new InvalidOperationException("Derived token late refresh probe.");
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
