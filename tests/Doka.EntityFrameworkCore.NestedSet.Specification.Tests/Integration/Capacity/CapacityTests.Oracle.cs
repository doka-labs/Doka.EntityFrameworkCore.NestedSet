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
    public async Task CoordinateOracleRecognizesCompleteFixtures(
        int count,
        bool deep,
        bool corrupt
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, count, deep, corrupt);
        await using var context = database.CreateContext();

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(context, count, deep, corrupt);

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
    public async Task CoordinateOracleDetectsMissingRows(
        int key
    )
    {
        // Arrange
        var count = key == 1 ? 1 : 100_001;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, count);
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == key)
            .ExecuteDeleteAsync(CancellationToken.None);

        await using var context = database.CreateContext();

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(context, count);

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
    public async Task CoordinateOracleDetectsUnexpectedRows(
        int key,
        bool replace
    )
    {
        // Arrange
        const int count = 129;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, count);

        if (replace)
        {
            await setup
                .Set<TreeNode>()
                .Where(node => node.NodeId == count)
                .ExecuteDeleteAsync(CancellationToken.None);
        }

        setup.Add(new TreeNode
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

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(context, count);

        // Assert
        Assert.Equal(replace ? 2 : 1, mismatches);
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
    public async Task CoordinateOracleDetectsStructuralDamage(
        string field,
        bool root,
        bool deep
    )
    {
        // Arrange
        var count = field == nameof(TreeNode.Tree) ? 1 : 129;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, count, deep);
        var target = setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == (root ? 1 : count));

        await DamageAsync(target, field, root);
        await using var context = database.CreateContext();

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(context, count, deep);

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
    public async Task CoordinateOracleDetectsWrongNonNullParent(
        bool deep
    )
    {
        // Arrange
        const int count = 129;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, count, deep);
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == count)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.Parent, deep ? 1 : 2), CancellationToken.None);

        await using var context = database.CreateContext();

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(context, count, deep);

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
    public async Task CoordinateOracleDetectsDamageAcrossRangeBoundary(
        int key
    )
    {
        // Arrange
        const int count = 100_001;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, count);
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == key)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Depth, 9), CancellationToken.None);

        await using var context = database.CreateContext();

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(context, count);

        // Assert
        Assert.Equal(1, mismatches);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Coordinate aggregates have a bounded primary-key range rather than a table-wide result wait.</summary>
    /// <returns>A task that completes after observing each bounded aggregate and its ordinary timeout.</returns>
    [Trait("Category", "Capacity")]
    [Fact]
    public async Task CoordinateOracleBoundsCoordinateAggregates()
    {
        // Arrange
        const int count = 100_001;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = database.CreateContext();
        await CapacitySeed.SeedAsync(setup, count);
        var probe = new CoordinateCommandProbe();
        await using var context = database.CreateContext(probe);
        await using var sample = context.Database.GetDbConnection().CreateCommand();
        var timeout = context.Database.GetCommandTimeout() ?? sample.CommandTimeout;

        // Act
        var mismatches = await CapacitySeed.MismatchesAsync(context, count);

        // Assert
        Assert.Equal(0, mismatches);
        var coordinates = probe.Commands
            .Where(command => command.Sql.Contains(nameof(TreeNode.Start), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var command in coordinates)
        {
            _output.WriteLine(command.Sql);
        }

        Assert.NotEmpty(coordinates);
        Assert.All(coordinates, command =>
        {
            Assert.Equal(2, command.Bounds.Length);
            Assert.InRange((long)command.Bounds[1] - command.Bounds[0] + 1, 1, 100_000);
            Assert.Equal(timeout, command.Timeout);
        });
    }

    /// <summary>Changes one stored field without repairing the deliberately invalid fixture row.</summary>
    private static Task<int> DamageAsync(
        IQueryable<TreeNode> target,
        string field,
        bool root
    ) => field switch
    {
        nameof(TreeNode.Tree) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Tree, 2), CancellationToken.None),
        nameof(TreeNode.TreeId) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.TreeId, s_inputTreeId), CancellationToken.None),
        nameof(TreeNode.Parent) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Parent, root ? 129 : (int?)null),
            CancellationToken.None),
        nameof(TreeNode.Start) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Start, 17L), CancellationToken.None),
        nameof(TreeNode.End) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.End, node => node.End + 1), CancellationToken.None),
        nameof(TreeNode.Depth) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Depth, 9), CancellationToken.None),
        nameof(TreeNode.Position) => target.ExecuteUpdateAsync(
            setters => setters.SetProperty(node => node.Position, 17L), CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    /// <summary>Retains structural aggregate SQL and key-range bindings for query verification.</summary>
    private sealed class CoordinateCommandProbe : DbCommandInterceptor
    {
        /// <summary>Gets the verification commands without retaining result rows or application payload.</summary>
        internal List<(string Sql, int[] Bounds, int Timeout)> Commands { get; } = [];

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add((
                command.CommandText,
                command.Parameters
                    .Cast<DbParameter>()
                    .Where(parameter => parameter.Value is int)
                    .Select(parameter => Convert.ToInt32(parameter.Value, CultureInfo.InvariantCulture))
                    .ToArray(),
                command.CommandTimeout));

            return ValueTask.FromResult(result);
        }
    }
}
