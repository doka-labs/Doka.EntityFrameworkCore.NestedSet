namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class CapacityTests
{
    /// <summary>The independent oracle recognizes canonical and deliberately unrepaired fixture states.</summary>
    /// <param name="count">The root-only, small, or multi-batch fixture size.</param>
    /// <param name="deep">Whether each nonroot node belongs to the preceding node.</param>
    /// <param name="corrupt">Whether the expected fixture retains its original unrepaired coordinates.</param>
    /// <returns>A task that completes after checking every stored structural field.</returns>
    [Trait("Category", "Capacity")]
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(1, true, true)]
    [InlineData(129, false, false)]
    [InlineData(129, true, false)]
    [InlineData(129, false, true)]
    [InlineData(129, true, true)]
    [InlineData(100_001, false, false)]
    [InlineData(100_001, true, false)]
    public async Task MismatchesAsync_CoordinateOracleRecognizes_CompleteFixtures(
        int count,
        bool deep,
        bool corrupt
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: deep,
            corrupt: corrupt);

        await using var context = CreateCapacityContext(database);

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(
            context,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: deep,
            corrupt: corrupt);

        // Assert
        Assert.Equal(0, mismatches);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Missing primary keys cannot pass merely because every remaining row has correct coordinates.</summary>
    /// <param name="key">A missing key before, at, or after the first range boundary.</param>
    /// <returns>A task that completes after detecting the absent expected key.</returns>
    [Trait("Category", "Capacity")]
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100_000)]
    [InlineData(100_001)]
    public async Task MismatchesAsync_CoordinateOracle_DetectsMissingRows(
        int key
    )
    {
        // Arrange
        var count = key == 1
            ? 1
            : 100_001;

        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == key)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        await using var context = CreateCapacityContext(database);

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(
            context,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);

        // Assert
        Assert.Equal(1, mismatches);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Unexpected keys remain visible even when an extra row replaces a missing expected row.</summary>
    /// <param name="key">The unexpected primary key below or above the fixture's expected range.</param>
    /// <param name="replace">Whether to remove the final expected node before inserting the extra row.</param>
    /// <returns>A task that completes after detecting every missing or unexpected key.</returns>
    [Trait("Category", "Capacity")]
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(130, false)]
    [InlineData(130, true)]
    public async Task MismatchesAsync_CoordinateOracle_DetectsUnexpectedRows(
        int key,
        bool replace
    )
    {
        // Arrange
        const int count = 129;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);

        if (replace)
        {
            await setup
                .Set<TreeNode>()
                .Where(node => node.NodeId == count)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        setup.Add(
            new TreeNode
            {
                NodeId = key,
                Tree = 1,
                TreeId = Guid.Empty,
                Parent = 1,
                Start = 260,
                End = 261,
                Depth = 1,
                Position = 128,
            });

        await setup.SavePrecomputedHierarchyAsync(TestContext.Current.CancellationToken);
        await using var context = CreateCapacityContext(database);

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(
            context,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);

        // Assert
        Assert.Equal(
            replace
                ? 2
                : 1,
            mismatches);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Each stored structural field is checked, including both directions of nullable parent damage.</summary>
    /// <param name="field">The persisted structural field changed independently of the public mutation API.</param>
    /// <param name="root">Whether to corrupt the root rather than the final child.</param>
    /// <param name="deep">Whether the expected adjacency is a chain rather than direct children.</param>
    /// <returns>A task that completes after detecting the damaged row.</returns>
    [Trait("Category", "Capacity")]
    [Theory]
    [InlineData(nameof(TreeNode.Tree), true, false)]
    [InlineData(nameof(TreeNode.TreeId), false, false)]
    [InlineData(nameof(TreeNode.Parent), true, false)]
    [InlineData(nameof(TreeNode.Parent), false, false)]
    [InlineData(nameof(TreeNode.Parent), false, true)]
    [InlineData(nameof(TreeNode.Start), true, false)]
    [InlineData(nameof(TreeNode.Start), false, true)]
    [InlineData(nameof(TreeNode.End), true, false)]
    [InlineData(nameof(TreeNode.End), false, true)]
    [InlineData(nameof(TreeNode.Depth), true, false)]
    [InlineData(nameof(TreeNode.Depth), false, true)]
    [InlineData(nameof(TreeNode.Position), true, false)]
    [InlineData(nameof(TreeNode.Position), false, true)]
    public async Task MismatchesAsync_CoordinateOracle_DetectsStructuralDamage(
        string field,
        bool root,
        bool deep
    )
    {
        // Arrange
        var count = field == nameof(TreeNode.Tree)
            ? 1
            : 129;

        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: deep,
            corrupt: false);
        var target = setup
            .Set<TreeNode>()
            .Where(node => node.NodeId
                == (root
                    ? 1
                    : count));

        await DamageAsync(target, field, root, cancellationToken: TestContext.Current.CancellationToken);
        await using var context = CreateCapacityContext(database);

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(
            context,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: deep,
            corrupt: false);

        // Assert
        Assert.Equal(1, mismatches);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An existing but incorrect parent cannot pass by satisfying the database foreign key.</summary>
    /// <param name="deep">Whether the correct parent is the preceding key rather than the root.</param>
    /// <returns>A task that completes after detecting the incorrect adjacency link.</returns>
    [Trait("Category", "Capacity")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchesAsync_CoordinateOracle_DetectsWrongNonNullParent(
        bool deep
    )
    {
        // Arrange
        const int count = 129;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: deep,
            corrupt: false);
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == count)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    node => node.Parent,
                    deep
                        ? 1
                        : 2),
                TestContext.Current.CancellationToken);

        await using var context = CreateCapacityContext(database);

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(
            context,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: deep,
            corrupt: false);

        // Assert
        Assert.Equal(1, mismatches);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Damage in the last row of either range remains part of complete fixture verification.</summary>
    /// <param name="key">The damaged key at or after the first range boundary.</param>
    /// <returns>A task that completes after detecting damage in the selected range.</returns>
    [Trait("Category", "Capacity")]
    [Theory]
    [InlineData(100_000)]
    [InlineData(100_001)]
    public async Task MismatchesAsync_CoordinateOracle_DetectsDamageAcrossRangeBoundary(
        int key
    )
    {
        // Arrange
        const int count = 100_001;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == key)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.Depth, 9),
                TestContext.Current.CancellationToken);

        await using var context = CreateCapacityContext(database);

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(
            context,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);

        // Assert
        Assert.Equal(1, mismatches);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Coordinate aggregates have a bounded primary-key range rather than a table-wide result wait.</summary>
    /// <returns>A task that completes after observing bounded aggregates without a command latency gate.</returns>
    [Trait("Category", "Capacity")]
    [Fact]
    public async Task MismatchesAsync_CoordinateOracle_BoundsCoordinateAggregates()
    {
        // Arrange
        const int count = 100_001;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);
        var probe = new CoordinateCommandProbe();
        await using var context = CreateCapacityContext(database, probe);

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(
            context,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);

        // Assert
        Assert.Equal(0, mismatches);
        var coordinates = probe
            .Commands
            .Where(command => command.Sql.Contains(nameof(TreeNode.Start), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var command in coordinates)
        {
            _output.WriteLine(command.Sql);
        }

        Assert.NotEmpty(coordinates);
        Assert.All(
            coordinates,
            command =>
            {
                Assert.Equal(2, command.Bounds.Length);
                Assert.InRange((long)command.Bounds[1] - command.Bounds[0] + 1, 1, 100_000);
                Assert.Equal(0, command.Timeout);
            });
    }
}
