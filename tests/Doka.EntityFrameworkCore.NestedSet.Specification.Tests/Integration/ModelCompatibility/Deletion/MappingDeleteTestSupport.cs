namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Shares deletion-case key allocation and physical-row checks across provider suites.</summary>
internal static class MappingDeleteTestSupport
{
    // WHY: Local and shared suites use the same collection-owned tables, so their key allocator must stay unique.
    private static int s_sequence;

    /// <summary>Allocates the next deletion case without colliding with another suite sharing its model.</summary>
    internal static int NextSequence() => Interlocked.Increment(ref s_sequence);

    /// <summary>Seeds an identified subtree and an unrelated tree using the common key allocator.</summary>
    internal static async Task<MappingDeleteScenario> SeedAsync<TNode>(
        DbContext context,
        Func<int, string, TNode> create,
        Func<DbContext, int, Task>? seedExtra
    )
        where TNode : class
    {
        var hierarchy = context.NestedSet<TNode>();
        var targetTree = Guid.NewGuid();
        var otherTree = Guid.NewGuid();
        var seed = 100_000 + (NextSequence() * 10);
        await hierarchy.InsertRootAsync(create(seed, "Root"), targetTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(create(seed + 1, "Branch"), seed, CancellationToken.None);
        await hierarchy.InsertChildAsync(create(seed + 2, "Leaf"), seed + 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(create(seed + 3, "Sibling"), seed, CancellationToken.None);
        await hierarchy.InsertRootAsync(create(seed + 4, "Other"), otherTree, CancellationToken.None);

        if (seedExtra is not null)
        {
            await seedExtra(context, seed);
        }

        return new MappingDeleteScenario(seed, targetTree, otherTree);
    }

    /// <summary>Counts physical mapping fragments inside one deletion case's allocated key range.</summary>
    internal static async Task<long> CountRowsAsync(
        DbContext context,
        string table,
        int seed,
        int lastOffset = 4
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {sql.DelimitIdentifier(table)} "
            + $"WHERE {sql.DelimitIdentifier("Id")} BETWEEN {seed} AND {seed + lastOffset}";

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);
    }
}
