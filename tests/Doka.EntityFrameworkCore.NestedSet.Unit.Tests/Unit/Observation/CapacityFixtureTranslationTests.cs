namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks real capacity-fixture updates against provider translation and independent endpoint values.</summary>
public sealed class CapacityFixtureTranslationTests
{
    /// <summary>The actual batched topology update translates before reaching the intentional I/O boundary.</summary>
    [Fact]
    public async Task BalancedTopologyTranslatesAtDefaultSqlServerCompatibility()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        await using var context = new TreeContext(SqlServerOptions(boundary));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CompositionCapacityProfiles.BalanceAsync(context, 100_001));

        // Assert
        Assert.Equal("Query construction attempted asynchronous database I/O.", failure.Message);
        Assert.Equal(1, boundary.ConnectionStarts);
    }

    /// <summary>The former minimum setter is rejected before I/O instead of silently evaluating client-side.</summary>
    [Fact]
    public async Task UnsupportedMinimumSetterFailsBeforeDatabaseIo()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        await using var context = new TreeContext(SqlServerOptions(boundary));
        const int nodes = 100_001;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Set<TreeNode>()
            .Where(node => node.NodeId > 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.End,
                    node => Math.Min((2L * nodes) - 1,
                        1L + (((long)Math.Floor((node.NodeId - 2) / 1_000d) + 1L) * 2_000L))),
                CancellationToken.None));

        // Assert
        Assert.Contains("could not be translated", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
    }

    /// <summary>Completed and truncated branches retain exact endpoints while the tree root stays unchanged.</summary>
    /// <param name="nodes">The independent wide-fixture population.</param>
    /// <param name="branch">The branch-root key whose right boundary is checked.</param>
    /// <param name="expectedRight">The right boundary derived from that branch's complete or partial population.</param>
    [Theory]
    [InlineData(2, 2, 3L)]
    [InlineData(1_001, 2, 2_001L)]
    [InlineData(1_002, 1_002, 2_003L)]
    [InlineData(1_003, 1_002, 2_005L)]
    [InlineData(2_001, 1_002, 4_001L)]
    public async Task BalancedBranchPreservesExactEndpoint(
        int nodes,
        int branch,
        long expectedRight
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync("Sqlite");
        await using var context = database.CreateContext();
        await CapacitySeed.SeedAsync(context, nodes);

        // Act
        var changed = await CompositionCapacityProfiles.BalanceAsync(context, nodes);
        var rows = await context.Set<TreeNode>()
            .AsNoTracking()
            .Where(node => node.NodeId == 1 || node.NodeId == branch)
            .OrderBy(node => node.NodeId)
            .Select(node => new { node.NodeId, node.Parent, node.Start, node.End, node.Depth, node.TreeId })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(nodes - 1, changed);
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[0].NodeId);
        Assert.Null(rows[0].Parent);
        Assert.Equal(1L, rows[0].Start);
        Assert.Equal(2L * nodes, rows[0].End);
        Assert.Equal(0, rows[0].Depth);
        Assert.Equal(Guid.Empty, rows[0].TreeId);
        Assert.Equal(branch, rows[1].NodeId);
        Assert.Equal(1, rows[1].Parent);
        Assert.Equal(expectedRight, rows[1].End);
        Assert.Equal(1, rows[1].Depth);
        Assert.Equal(Guid.Empty, rows[1].TreeId);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Uses the actual provider default without retaining a process-global provider graph.</summary>
    private static DbContextOptions<TreeContext> SqlServerOptions(
        CompositionIoBoundary boundary
    )
        => new DbContextOptionsBuilder<TreeContext>()
            .UseSqlServer(
                "Server=localhost;Database=capacity_translation;Integrated Security=true",
                provider => provider.UseNestedSets())
            .EnableServiceProviderCaching(false)
            .AddInterceptors(boundary)
            .Options;
}
