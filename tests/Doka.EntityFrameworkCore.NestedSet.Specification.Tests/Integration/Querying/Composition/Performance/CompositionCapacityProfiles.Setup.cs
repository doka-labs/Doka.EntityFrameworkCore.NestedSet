namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionCapacityProfiles
{
    /// <summary>Converts an existing wide fixture to equal-sized depth-two branches with bounded last branch.</summary>
    /// <remarks>
    /// WHY: Native arithmetic updates reuse the persisted population, avoid an additional million-node import,
    /// and retain exact adjacency while comparing genuinely different branching distributions.
    /// </remarks>
    internal static async Task<int> BalanceAsync(
        TreeContext context,
        int nodes,
        CancellationToken cancellationToken
    )
    {
        var changed = 0;

        // WHY: Updating every indexed boundary in one million-row statement holds an unbounded fixture command.
        // Primary-key windows keep setup bounded without changing the query timeout or seeding a second tree.
        for (var first = 1; first <= nodes; first += NativeMutationBatchSize)
        {
            var last = Math.Min(nodes, first + NativeMutationBatchSize - 1);
            changed += await context
                .Set<TreeNode>()
                .Where(node => node.NodeId > 1 && node.NodeId >= first && node.NodeId <= last)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            node => node.Parent,
                            node => (node.NodeId - 2) % BalancedBranchSize == 0
                                ? 1
                                : 2
                                + (int)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                                * BalancedBranchSize)
                        .SetProperty(
                            node => node.Start,
                            node => 2L
                                + (long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                                * 2L
                                * BalancedBranchSize
                                + ((node.NodeId - 2) % BalancedBranchSize == 0
                                    ? 0L
                                    : 2L * ((node.NodeId - 2) % BalancedBranchSize) - 1))

                        // WHY: CASE preserves the partial-branch cap at EF's default SQL Server compatibility;
                        // Math.Min requires a newer LEAST translation and would fail before fixture I/O.
                        .SetProperty(
                            node => node.End,
                            node => (node.NodeId - 2) % BalancedBranchSize == 0
                                ? 1L
                                + ((long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize) + 1L)
                                * 2L
                                * BalancedBranchSize
                                < 2L * nodes - 1
                                    ? 1L
                                    + ((long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize) + 1L)
                                    * 2L
                                    * BalancedBranchSize
                                    : 2L * nodes - 1
                                : 2L
                                + (long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                                * 2L
                                * BalancedBranchSize
                                + 2L * ((node.NodeId - 2) % BalancedBranchSize))
                        .SetProperty(
                            node => node.Depth,
                            node => (node.NodeId - 2) % BalancedBranchSize == 0
                                ? 1
                                : 2)
                        .SetProperty(
                            node => node.Position,
                            node => (node.NodeId - 2) % BalancedBranchSize == 0
                                ? (long)Math.Floor((node.NodeId - 2) / (double)BalancedBranchSize)
                                : (node.NodeId - 2) % BalancedBranchSize - 1),
                    cancellationToken);
        }

        return changed;
    }

    /// <summary>Restores wide adjacency and the original repair corruption before its heap observer starts.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="context">The existing fixture context outside the measured repair operation.</param>
    /// <param name="nodes">The complete existing population; no additional tree is seeded.</param>
    /// <returns>The number of existing rows changed across bounded primary-key windows.</returns>
    internal static async Task<int> CorruptWideForRepairAsync(
        TreeContext context,
        int nodes,
        CancellationToken cancellationToken
    )
    {
        var changed = 0;

        // WHY: Index maintenance for a million-row fixture reset must stay bounded just like native seeding.
        // This occurs before measurement and does not relax the public rebuild's original command or heap limits.
        for (var first = 1; first <= nodes; first += NativeMutationBatchSize)
        {
            var last = Math.Min(nodes, first + NativeMutationBatchSize - 1);
            changed += await context
                .Set<TreeNode>()
                .Where(node => node.NodeId >= first && node.NodeId <= last)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            node => node.Parent,
                            node => node.NodeId == 1
                                ? null
                                : 1)
                        .SetProperty(
                            node => node.Position,
                            node => node.NodeId == 1
                                ? 0L
                                : node.NodeId - 2L)
                        .SetProperty(node => node.Start, 1L)
                        .SetProperty(node => node.End, 2L)
                        .SetProperty(node => node.Depth, 9),
                    cancellationToken);
        }

        return changed;
    }

    /// <summary>Copies only the requested scalar payload keys with mapped table names and bounded SQL.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="context">The case-owned database with its already seeded hierarchy.</param>
    /// <param name="nodes">The largest source key in the fixture's dense population.</param>
    /// <param name="facts">The number of trailing hierarchy keys that receive payload.</param>
    /// <returns>A task completing after every bounded insert has finished.</returns>
    internal static async Task SeedPayloadAsync(
        TreeContext context,
        int nodes,
        int facts,
        CancellationToken cancellationToken
    )
    {
        await context
            .Set<UnrelatedRow>()
            .ExecuteDeleteAsync(cancellationToken);

        var sql = context.GetService<ISqlGenerationHelper>();
        var source = context.Model.FindEntityType(typeof(TreeNode))!;
        var target = context.Model.FindEntityType(typeof(UnrelatedRow))!;
        var sourceStore = StoreObjectIdentifier.Table(source.GetTableName()!, source.GetSchema());
        var targetStore = StoreObjectIdentifier.Table(target.GetTableName()!, target.GetSchema());
        var sourceKey = sql.DelimitIdentifier(
            source.FindProperty(nameof(TreeNode.NodeId))!
                .GetColumnName(sourceStore)!);

        var targetKey = sql.DelimitIdentifier(
            target.FindProperty(nameof(UnrelatedRow.Id))!
                .GetColumnName(targetStore)!);

        var value = sql.DelimitIdentifier(
            target.FindProperty(nameof(UnrelatedRow.Value))!
                .GetColumnName(targetStore)!);

        // WHY: A broad ten-million-row fixture must not become one extra unbounded INSERT before
        // the measured query. Reuse the existing primary-key window size without changing facts or timeouts.
        for (var first = (long)nodes - facts + 1; first <= nodes; first += NativeMutationBatchSize)
        {
            var last = Math.Min(nodes, first + NativeMutationBatchSize - 1);
            var statement = $"INSERT INTO {sql.DelimitIdentifier(targetStore.Name, targetStore.Schema)} "
                + $"({targetKey}, {value}) SELECT {sourceKey}, 'fact' "
                + $"FROM {sql.DelimitIdentifier(sourceStore.Name, sourceStore.Schema)} "
                + $"WHERE {sourceKey} >= {first.ToString(CultureInfo.InvariantCulture)} "
                + $"AND {sourceKey} <= {last.ToString(CultureInfo.InvariantCulture)}";

            // WHY: Only fixture-owned integer bounds are interpolated; identifiers use the provider delimiter.
            await context.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }
}
