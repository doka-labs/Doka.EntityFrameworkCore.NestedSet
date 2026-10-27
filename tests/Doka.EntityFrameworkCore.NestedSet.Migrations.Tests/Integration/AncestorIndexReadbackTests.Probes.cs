using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexReadbackTests
{
    /// <summary>Captures one complete database or caller-visible hierarchy row.</summary>
    /// <param name="Id">The supplied primary key.</param>
    /// <param name="Scope">The isolation scope.</param>
    /// <param name="TreeId">The stable tree identity.</param>
    /// <param name="ParentId">The immediate parent.</param>
    /// <param name="Left">The left interval boundary.</param>
    /// <param name="Right">The right interval boundary.</param>
    /// <param name="Depth">The structural depth.</param>
    /// <param name="Position">The sibling position.</param>
    /// <param name="Input">The application-owned computation input.</param>
    /// <param name="Stamp">The row-distinct generated output.</param>
    /// <param name="IsBranch">The generated shadow marker.</param>
    private sealed record ReadbackRow(
        int Id,
        int Scope,
        int TreeId,
        int? ParentId,
        long Left,
        long Right,
        int Depth,
        long Position,
        int Input,
        long Stamp,
        bool IsBranch
    );

    /// <summary>Shares native SQL and completed payload observations without changing provider commands.</summary>
    private sealed class ReadbackObservation
    {
        /// <summary>The deliberate late failure distinguished from provider and rollback failures.</summary>
        internal const string FailureMessage = "Deliberate failure after later generated readback.";

        /// <summary>Gets or sets whether the operation under test is being observed.</summary>
        internal bool Enabled { get; set; }

        /// <summary>Gets or sets whether a later completed payload save should fail.</summary>
        internal bool FailLaterSave { get; init; }

        /// <summary>Gets or sets the caller-owned cancellation source for the cancellation failure case.</summary>
        internal CancellationTokenSource? Cancellation { get; init; }

        /// <summary>Gets the number of observed completed public import payload saves.</summary>
        internal int PayloadSaves { get; set; }

        /// <summary>Gets the exact unmodified native insert/readback command text.</summary>
        internal List<string> Commands { get; } = [];

        /// <summary>Gets each generated result at its payload-save boundary before final scalar refresh.</summary>
        internal List<ReadbackRow> SavedRows { get; } = [];
    }

    /// <summary>Observes actual insert statements without rewriting SQL or consuming generated results.</summary>
    private sealed class ReadbackCommandProbe(ReadbackObservation observation) : DbCommandInterceptor
    {
        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (observation.Enabled
                && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal)
                && command.CommandText.Contains(AncestorIndexReadbackContext.TableName, StringComparison.Ordinal))
            {
                observation.Commands.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Captures actual EF-generated outputs and interrupts a later already executed payload save.</summary>
    private sealed class ReadbackSaveProbe(ReadbackObservation observation) : SaveChangesInterceptor
    {
        /// <inheritdoc />
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!observation.Enabled)
            {
                return result;
            }

            var entries = eventData.Context!
                .ChangeTracker
                .Entries<AncestorIndexReadbackNode>()
                .ToArray();

            if (entries.Length == 0)
            {
                return result;
            }

            observation.PayloadSaves++;

            foreach (var entry in entries)
            {
                // WHY: The coordinated context saves with acceptance disabled. Generated CLR values still live
                // in EF's store-generated sidecar at this callback; the entity retains its original field value.
                observation.SavedRows.Add(
                    Capture(
                            entry.Entity,
                            entry.Property<bool>(AncestorIndexReadbackContext.FlagProperty)
                                .CurrentValue) with
                        {
                            Stamp = entry.Property(node => node.Stamp).CurrentValue,
                        });
            }

            // WHY: Waiting for a later completed save proves both an earlier batch and this batch reached the
            // server. Rollback must restore values already propagated by EF, not merely reject untouched input.
            if (observation is { FailLaterSave: true, PayloadSaves: >= 2 })
            {
                if (observation.Cancellation is not null)
                {
                    await observation.Cancellation.CancelAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                throw new InvalidOperationException(ReadbackObservation.FailureMessage);
            }

            return result;
        }
    }
}
