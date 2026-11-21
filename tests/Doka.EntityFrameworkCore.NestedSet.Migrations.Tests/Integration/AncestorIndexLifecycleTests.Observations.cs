using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexLifecycleTests
{

    /// <summary>Creates two tracked trees, including one leaf root and a three-level branch.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static async Task SeedAsync(
        AncestorIndexContext context,
        CancellationToken cancellationToken
    )
    {
        var hierarchy = context
            .NestedSet<MigrationNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new MigrationNode { NodeId = 1 }, 11, cancellationToken);
        await hierarchy.InsertChildAsync(new MigrationNode { NodeId = 2 }, 1, cancellationToken);
        await hierarchy.InsertChildAsync(new MigrationNode { NodeId = 3 }, 2, cancellationToken);
        await hierarchy.InsertRootAsync(new MigrationNode { NodeId = 4 }, 12, cancellationToken);
    }

    /// <summary>Reads database values without identity resolution hiding stale tracker values.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    private static Task<FlagState[]> ReadStatesAsync(
        AncestorIndexContext context,
        CancellationToken cancellationToken
    ) => context
        .Set<MigrationNode>()
        .AsNoTracking()
        .OrderBy(x => x.NodeId)
        .Select(x => new FlagState(x.NodeId, x.Left, x.Right, EF.Property<bool>(x, AncestorIndexContext.FlagProperty)))
        .ToArrayAsync(cancellationToken);

    /// <summary>Captures generated shadow sidecars without any database I/O.</summary>
    private static FlagState[] ReadTrackedStates(
        AncestorIndexContext context
    ) => context
        .ChangeTracker
        .Entries<MigrationNode>()
        .Where(x => x.State is not EntityState.Detached and not EntityState.Deleted)
        .Select(x => new FlagState(
            x.Entity.NodeId,
            x.Entity.Left,
            x.Entity.Right,
            x.Property<bool>(AncestorIndexContext.FlagProperty)
                .CurrentValue))
        .OrderBy(x => x.Id)
        .ToArray();

    /// <summary>A detached comparison of persisted or tracked bounds and the generated shadow value.</summary>
    /// <param name="Id">The application-assigned node key.</param>
    /// <param name="Left">The current left boundary.</param>
    /// <param name="Right">The current right boundary.</param>
    /// <param name="Flag">The database-derived branch marker.</param>
    private sealed record FlagState(
        int Id,
        long Left,
        long Right,
        bool Flag
    );

    /// <summary>Fails after the payload has been persisted inside the library-owned transaction boundary.</summary>
    private sealed class LateSaveFailure : SaveChangesInterceptor
    {
        /// <summary>The exact deliberate failure used to distinguish rollback from an unrelated exception.</summary>
        internal const string Message = "Deliberate ancestor-index payload failure.";

        /// <summary>Gets or sets whether the next payload-save completion must fail.</summary>
        internal bool Armed { get; set; }

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        ) => Armed
            ? throw new InvalidOperationException(Message)
            : ValueTask.FromResult(result);
    }
}
