namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Checks real sibling-permutation commands for bounded interval seeks and untouched gaps.</summary>
public sealed class SiblingPermutationRangeTests : ProviderTest,
    IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    private readonly ITestOutputHelper _output;

    /// <summary>Uses isolated ordering tables and records the actual SQLite execution plans.</summary>
    /// <param name="fixture">The immutable SQLite owner; individual tests own their database resources.</param>
    /// <param name="output">The sink recording measured execution evidence.</param>
    public SiblingPermutationRangeTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>Seeks every full and tail mark/restore batch while leaving disjoint gaps unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionPermutationBatchesSeekRightBoundsAndPreserveGaps(
        bool analyzed
    )
    {
        // Arrange
        // WHY: Independent databases keep collected statistics from leaking into the fresh-statistics case.
        await using var fixture = new OrderingFixture();
        await fixture.InitializeAsync();
        await using var setup = await fixture.ResetAsync(Engine);
        await SeedAsync(setup);
        var before = await setup
            .Set<OrderingNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        if (analyzed)
        {
            await setup.Database.ExecuteSqlRawAsync("ANALYZE", CancellationToken.None);
        }

        var probe = new EnterpriseProbe("StrictOrderingNodes", captureParameterBindings: true);
        await using var context = await fixture.CreateContextAsync(Engine, "Strict", probe);
        var changed = await context
            .Set<OrderingNode>()
            .Where(node => node.ParentId == 1 && node.Position < 195 && node.Position % 3 == 0)
            .ToArrayAsync(CancellationToken.None);

        foreach (var node in changed)
        {
            // WHY: Each pair swaps around its unchanged third sibling; 130 moves span full and tail batches.
            node.Name = $"N{node.Position / 3:D4}-bz";
        }

        // Act
        var saved = await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        var commands = Enumerable
            .Range(0, probe.Commands.Count)
            .Where(index => probe
                    .Commands[index]
                    .TrimStart()
                    .StartsWith("UPDATE", StringComparison.Ordinal)
                && probe
                    .Commands[index]
                    .Contains("@m", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(65, saved);
        Assert.Equal(6, commands.Length);
        Assert.Equal(
            3,
            commands.Count(index => probe
                .Commands[index]
                .Contains("CASE", StringComparison.Ordinal)));
        Assert.InRange(probe.MaximumUpdateParameters, 1, 322);

        foreach (var index in commands)
        {
            var plan = await Specifications.ServerQueryPlanTests.ExplainAsync(context, probe, index, Engine);
            _output.WriteLine(probe.Commands[index]);
            _output.WriteLine(plan);
            Assert.Contains("SEARCH StrictOrderingNodes", plan, StringComparison.Ordinal);
            Assert.Contains("Right>?", plan, StringComparison.Ordinal);
            Assert.Contains("Right<?", plan, StringComparison.Ordinal);
        }

        var after = await setup
            .Set<OrderingNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(
            before
                .Where(IsUnchanged)
                .Select(Coordinates),
            after
                .Where(IsUnchanged)
                .Select(Coordinates));
        Assert.Equal(
            Enumerable
                .Range(1, after.Length * 2)
                .Select(value => (long)value),
            after
                .SelectMany(node => new[] { node.Left, node.Right })
                .Order());

        for (var index = 0; index < 195; index++)
        {
            var expectedPosition = (index % 3) switch
            {
                0 => index + 1,
                1 => index - 1,
                _ => index,
            };
            Assert.Equal(expectedPosition, after[index + 1].Position);
        }
    }

    /// <summary>Seeds 4,097 valid nodes without measuring hierarchy construction or automatic ordering.</summary>
    private static async Task SeedAsync(
        OrderingContext context
    )
    {
        var treeId = Guid.NewGuid();
        var children = Enumerable
            .Range(0, 4096)
            .Select(position => new OrderingNode
            {
                Id = position + 2,
                Scope = 1,
                TreeId = treeId,
                ParentId = 1,
                Name = $"N{position / 3:D4}-{(char)('a' + (position % 3))}",
                Left = (position * 2L) + 2,
                Right = (position * 2L) + 3,
                Depth = 1,
                Position = position,
            });

        var root = new OrderingNode
        {
            Id = 1,
            Scope = 1,
            TreeId = treeId,
            Name = "Root",
            Left = 1,
            Right = 8194,
        };

        await context.AddRangeAsync(children.Prepend(root), CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }

    /// <summary>Includes the root, separated third siblings, and the entire untouched suffix.</summary>
    private static bool IsUnchanged(
        OrderingNode node
    ) => node.ParentId is null || node.Position >= 195 || node.Id % 3 == 1;

    /// <summary>Compares hierarchy values without depending on entity reference identity.</summary>
    private static (int Id, long Left, long Right, long Position) Coordinates(
        OrderingNode node
    ) => (node.Id, node.Left, node.Right, node.Position);
}
