namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies persisted capacity, public bulk atomicity, and bounded repair memory on each engine.</summary>
// WHY: Fresh case-owned databases isolate statistics and avoid deleting preceding capacity datasets during setup.
public abstract partial class CapacityTests : ProviderTest
{
    private const int Million = 1_000_000;
    private const long AdditionalHeapBudget = 512L * 1024 * 1024;
    private static readonly Guid s_inputTreeId = new("11111111-1111-1111-1111-111111111111");
    private readonly ITestOutputHelper _output;

    /// <summary>Binds the provider engine and records capacity measurements in test output.</summary>
    /// <param name="fixture">The fixture supplying this suite's immutable engine identity.</param>
    /// <param name="output">The sink retaining measured evidence.</param>
    protected CapacityTests(
        IProviderFixture<RelationalFixture> fixture,
        ITestOutputHelper output
    ) : base(fixture)
    {
        _output = output;
    }

    /// <summary>Checks native mapped seeding and the complete observer path at a manageable boundary size.</summary>
    /// <param name="deep">Whether native seeding creates a chain instead of direct children.</param>
    /// <returns>A task that completes after independent stored-coordinate and repair checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Capacity")]
    public async Task NativeSeedAndRepairPreserveMappedIdentity(
        bool deep
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await WarmAsync(database);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(setup, 129, deep, corrupt: true);
        var registry = await CapacitySeed.RegistryAsync(setup);
        await using var probe = new CapacityProbe();
        await using var context = CreateCapacityContext(database, probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        probe.Start();

        // Act
        try
        {
            await tree.RebuildAsync(CancellationToken.None);
        }
        finally
        {
            await probe.StopAsync();
            WriteMeasurement("observer qualification", 129, probe);
        }

        // Assert
        Assert.True(probe.Samples > 0);
        Assert.True(probe.TotalAllocatedBytes > 0);
        Assert.InRange(probe.AdditionalOccupiedBytes, 0, AdditionalHeapBudget);
        Assert.Equal(3, probe.RepairCommands);
        Assert.Equal(3, probe.CompletedRepairs);
        Assert.Equal(129, probe.RepairedRows);
        await AssertCanonicalAsync(database, 129, deep);
        Assert.Equal(registry with { Revision = registry.Revision + 1 }, await CapacitySeed.RegistryAsync(setup));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Reads every one of ten million persisted nodes and fully validates their stored adjacency.</summary>
    /// <returns>A task that completes after streaming readability and full structural validation.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task TenMillionPersistedNodesAreReadableAndFullyValid()
    {
        // Arrange
        const int count = 10 * Million;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(setup, count);
        await using var context = CreateCapacityContext(database);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var readCount = 0L;
        var keySum = 0L;

        // Act
        await foreach (var key in tree
                           .Nodes
                           .AsNoTracking()
                           .Select(node => node.NodeId)
                           .AsAsyncEnumerable()
                           .WithCancellation(CancellationToken.None))
        {
            readCount++;
            keySum += key;
        }

        var report = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        _output.WriteLine($"Engine={Engine}; persisted/read/full-validation nodes={count}; read count={readCount}");
        Assert.Equal(count, readCount);
        Assert.Equal(((long)count * (count + 1)) / 2, keySum);
        Assert.True(report.IsValid);
        Assert.Equal(count, report.NodeCount);
        Assert.Empty(report.Issues);
        Assert.Equal(0, await CapacitySeed.MismatchesAsync(context, count));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Atomically imports one million direct children through the public forest API.</summary>
    /// <returns>A task that completes after measured import and independent full persisted validation.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task MillionDirectChildrenImportWithinAdditionalHeapBudget()
    {
        // Arrange
        const int count = Million + 1;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await WarmAsync(database);
        await using var probe = new CapacityProbe();
        await using var context = CreateCapacityContext(database, probe);
        context.SavedChanges += (_, _) => probe.Saved();
        var branch = CreateWide(count);
        var imports = new[] { new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, branch) };
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        probe.Start();

        // Act
        try
        {
            await hierarchy.InsertForestAsync(imports, CancellationToken.None);
        }
        finally
        {
            await probe.StopAsync();
            GC.KeepAlive(imports);
            WriteMeasurement("public forest import", count, probe);
        }

        // Assert
        Assert.InRange(probe.AdditionalOccupiedBytes, 0, AdditionalHeapBudget);
        Assert.Equal((count + 63) / 64, probe.CompletedSaves);
        Assert.Equal(0, probe.RepairCommands);
        Assert.InRange(probe.MaximumCommandParameters, 1, 999);
        Assert.Equal(Million, branch.Children.Count);
        var root = branch.Entity;
        Assert.Equal(
            (1L, 2L * count, 0, 0L, (int?)null),
            (root.Start, root.End, root.Depth, root.Position, root.Parent));
        Assert.Equal(0, CountInputMismatches(branch, restored: false));
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertCanonicalAsync(database, count);
    }

    /// <summary>Fully validates and rebuilds a real depth-100000 chain without recursive traversal.</summary>
    /// <returns>A task that completes after persisted deep-chain validation and measured public rebuild.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task DepthOneHundredThousandValidatesAndRebuilds()
    {
        // Arrange
        const int count = 100_001;
        await using var database = await TestDatabase.CreateAsync(Engine);
        await WarmAsync(database);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(setup, count, deep: true);
        var tree = setup
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var before = await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        await setup
            .Set<TreeNode>()
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(node => node.Start, 1L)
                    .SetProperty(node => node.End, 2L)
                    .SetProperty(node => node.Depth, 9),
                CancellationToken.None);

        await using var probe = new CapacityProbe();
        await using var context = CreateCapacityContext(database, probe);
        var selected = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        probe.Start();

        // Act
        try
        {
            await selected.RebuildAsync(CancellationToken.None);
        }
        finally
        {
            await probe.StopAsync();
            WriteMeasurement("deep public rebuild", count, probe);
        }

        // Assert
        Assert.True(before.IsValid);
        Assert.Equal(count, before.NodeCount);
        Assert.InRange(probe.AdditionalOccupiedBytes, 0, AdditionalHeapBudget);
        Assert.Equal((count + 63) / 64, probe.CompletedRepairs);
        Assert.Equal(count, probe.RepairedRows);
        Assert.InRange(probe.MaximumRepairParameters, 1, 322);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertCanonicalAsync(database, count, deep: true);
        Assert.Equal(
            100_000,
            await setup
                .Set<TreeNode>()
                .MaxAsync(node => node.Depth, CancellationToken.None));
    }

    /// <summary>Rebuilds one million corrupted nodes within the additional occupied-heap budget.</summary>
    /// <returns>A task that completes after measured public repair and full persisted validation.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task MillionNodeRebuildWithinAdditionalHeapBudget()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await WarmAsync(database);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(setup, Million, corrupt: true);
        var registry = await CapacitySeed.RegistryAsync(setup);
        await using var probe = new CapacityProbe();
        await using var context = CreateCapacityContext(database, probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        probe.Start();

        // Act
        try
        {
            await tree.RebuildAsync(CancellationToken.None);
        }
        finally
        {
            await probe.StopAsync();
            WriteMeasurement("million-node public rebuild", Million, probe);
        }

        // Assert
        Assert.InRange(probe.AdditionalOccupiedBytes, 0, AdditionalHeapBudget);
        Assert.Equal(15_625, probe.RepairCommands);
        Assert.Equal(15_625, probe.CompletedRepairs);
        Assert.Equal(Million, probe.RepairedRows);
        Assert.InRange(probe.MaximumRepairParameters, 1, 322);
        Assert.InRange(probe.MaximumRepairSqlLength, 1, 20_000);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertCanonicalAsync(database, Million);
        Assert.Equal(registry with { Revision = registry.Revision + 1 }, await CapacitySeed.RegistryAsync(setup));
    }

    /// <summary>A final-refresh fault restores all million imported inputs and rolls back every payload wave.</summary>
    /// <param name="count">The manageable observer check or the full million-node capacity workload.</param>
    /// <param name="cancel">Whether the late fault is cancellation instead of an injected command failure.</param>
    /// <returns>A task that completes after complete input, database, and registry rollback checks.</returns>
    [Theory]
    [InlineData(129, false)]
    [InlineData(129, true)]
    [InlineData(Million, false)]
    [InlineData(Million, true)]
    [Trait("Category", "Capacity")]
    public async Task MillionNodeLateImportFailureRestoresInputsAndRegistry(
        int count,
        bool cancel
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await WarmAsync(database);
        using var cancellation = new CancellationTokenSource();
        await using var probe = new CapacityProbe();
        probe.FailBulkRefresh = true;
        probe.Cancellation = cancel ? cancellation : null;
        await using var context = CreateCapacityContext(database, probe);
        context.SavedChanges += (_, _) => probe.Saved();
        var branch = CreateWide(count);
        var imports = new[] { new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, branch) };
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertForestAsync(imports, cancellation.Token));

        // Assert
        AssertFailure(error, cancel);
        Assert.True(probe.ReachedFailure);
        Assert.Equal((count + 63) / 64, probe.CompletedSaves);
        Assert.Equal(0, CountInputMismatches(branch, restored: true));
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = CreateCapacityContext(database);
        Assert.Equal(
            0,
            await verification
                .Set<TreeNode>()
                .LongCountAsync(CancellationToken.None));
        Assert.Equal(0, await CapacitySeed.RegistryCountAsync(verification));
        _output.WriteLine(
            $"Engine={Engine}; nodes={count}; canceled={cancel}; "
            + $"payload waves before final refresh failure={probe.CompletedSaves}; inputs and reservation restored");
    }

    /// <summary>The last of 15625 repair batches can fail without persisting any earlier repair or revision.</summary>
    /// <param name="count">The manageable observer check or the full million-node capacity workload.</param>
    /// <param name="cancel">Whether the last repair boundary cancels instead of throwing an injected failure.</param>
    /// <returns>A task that completes after exact persisted-coordinate and registry rollback checks.</returns>
    [Theory]
    [InlineData(129, false)]
    [InlineData(129, true)]
    [InlineData(Million, false)]
    [InlineData(Million, true)]
    [Trait("Category", "Capacity")]
    public async Task MillionNodeLastRepairBatchFailureRollsBackEarlierBatches(
        int count,
        bool cancel
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        await WarmAsync(database);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(setup, count, corrupt: true);
        var registry = await CapacitySeed.RegistryAsync(setup);
        using var cancellation = new CancellationTokenSource();
        await using var probe = new CapacityProbe();
        probe.FailRepairCommand = (count + 63) / 64;
        probe.Cancellation = cancel ? cancellation : null;
        await using var context = CreateCapacityContext(database, probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var error = await Record.ExceptionAsync(() => tree.RebuildAsync(cancellation.Token));

        // Assert
        AssertFailure(error, cancel);
        Assert.True(probe.ReachedFailure);
        Assert.Equal((count + 63) / 64, probe.RepairCommands);
        Assert.Equal(((count + 63) / 64) - 1, probe.CompletedRepairs);
        Assert.Equal((long)probe.CompletedRepairs * 64, probe.RepairedRows);
        Assert.InRange(probe.MaximumRepairParameters, 1, 322);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = CreateCapacityContext(database);
        Assert.Equal(
            count,
            await verification
                .Set<TreeNode>()
                .LongCountAsync(CancellationToken.None));
        Assert.Equal(0, await CapacitySeed.MismatchesAsync(verification, count, corrupt: true));
        Assert.Equal(registry, await CapacitySeed.RegistryAsync(verification));
        _output.WriteLine(
            $"Engine={Engine}; nodes={count}; canceled={cancel}; "
            + $"completed repair batches before last failure={probe.CompletedRepairs}; rows and revision restored");
    }

    /// <summary>Creates a capacity context without imposing a SQL Server undo-latency target.</summary>
    /// <param name="database">The case-owned database.</param>
    /// <param name="probe">The optional command observer for this context.</param>
    /// <returns>An unopened context preserving the ordinary command timeout.</returns>
    private static TreeContext CreateCapacityContext(
        TestDatabase database,
        CapacityProbe? probe = null
    )
    {
        var context = probe is null ? database.CreateContext() : database.CreateContext(probe);

        if (context.Database.GetDbConnection() is SqlConnection connection)
        {
            // WHY: Native rollback inherits SqlClient's connection timeout; capacity has no CPU or undo latency target.
            // The test runner owns end-to-end stopping.
            var options = new SqlConnectionStringBuilder(connection.ConnectionString)
            {
                ConnectTimeout = 0,
            };

            connection.ConnectionString = options.ConnectionString;
        }

        return context;
    }

    /// <summary>Warms the same public import, inspection, and repair paths before measuring capacity work.</summary>
    private static async Task WarmAsync(
        TestDatabase database
    )
    {
        await using var context = CreateCapacityContext(database);
        await context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, CreateWide(65))],
                CancellationToken.None);

        await context
            .Set<TreeNode>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Start, 1L), CancellationToken.None);

        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        await tree.RebuildAsync(CancellationToken.None);
        await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        await database.ResetAsync();
    }

    /// <summary>Builds all caller-owned topology and application payload before the measured baseline.</summary>
    private static NestedSetBranch<TreeNode> CreateWide(
        int count
    )
    {
        var children = new NestedSetBranch<TreeNode>[count - 1];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = new NestedSetBranch<TreeNode>(CreateInput(index + 2));
        }

        return new NestedSetBranch<TreeNode>(CreateInput(1), children);
    }

    /// <summary>Gives each input original structure so rollback cannot pass through default values.</summary>
    private static TreeNode CreateInput(
        int key
    ) => new()
    {
        NodeId = key,
        TreeId = s_inputTreeId,
        Tree = 7,
        Start = 17,
        End = 18,
        Depth = 9,
        Position = 11,
        Parent = 999,
        Payload = "caller-owned capacity payload",
    };

    /// <summary>Verifies every input with aggregate mismatches without allocating another full snapshot.</summary>
    private static int CountInputMismatches(
        NestedSetBranch<TreeNode> branch,
        bool restored
    )
    {
        var count = branch.Children.Count + 1;
        var mismatches = Matches(branch.Entity, 1) ? 0 : 1;
        for (var index = 0; index < branch.Children.Count; index++)
        {
            if (!Matches(branch.Children[index].Entity, index + 2))
            {
                mismatches++;
            }
        }

        return mismatches;

        bool Matches(
            TreeNode node,
            int key
        ) => node.NodeId == key
            && node.Payload == "caller-owned capacity payload"
            && node.TreeId == (restored ? s_inputTreeId : Guid.Empty)
            && node.Tree == (restored ? 7 : 1)
            && node.Start == (restored ? 17 : key == 1 ? 1L : (2L * key) - 2)
            && node.End == (restored ? 18 : key == 1 ? 2L * count : (2L * key) - 1)
            && node.Depth == (restored ? 9 : key == 1 ? 0 : 1)
            && node.Position == (restored ? 11 : key == 1 ? 0L : key - 2L)
            && node.Parent == (restored ? 999 : key == 1 ? null : 1);
    }

    /// <summary>Checks the exact persisted result through a fresh context and public Full validation.</summary>
    private static async Task AssertCanonicalAsync(
        TestDatabase database,
        int count,
        bool deep = false
    )
    {
        await using var verification = CreateCapacityContext(database);
        Assert.Equal(
            count,
            await verification
                .Set<TreeNode>()
                .LongCountAsync(CancellationToken.None));
        Assert.Equal(0, await CapacitySeed.MismatchesAsync(verification, count, deep));

        var report = await verification
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
        Assert.True(report.IsValid);
        Assert.Equal(count, report.NodeCount);
        Assert.Empty(report.Issues);
    }

    /// <summary>Checks the exact injected failure class independently of the rollback assertions.</summary>
    private static void AssertFailure(
        Exception? error,
        bool cancel
    )
    {
        if (cancel)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        }
        else
        {
            Assert.IsType<InjectedCommandException>(error);
        }
    }

    /// <summary>Reports sampled occupied heap separately from total allocation without claiming exact peaks.</summary>
    private void WriteMeasurement(
        string operation,
        int count,
        CapacityProbe probe
    ) => _output.WriteLine(
        $"Engine={Engine}; operation={operation}; nodes={count}; "
        + $"maximum observed additional occupied heap bytes={probe.AdditionalOccupiedBytes}; "
        + $"total allocated bytes={probe.TotalAllocatedBytes}; samples={probe.Samples}; "
        + $"completed saves={probe.CompletedSaves}; completed repairs={probe.CompletedRepairs}; "
        + "baseline excludes pre-existing caller input; provider/framework allocations are included; "
        + "5ms and command-boundary sampling is not an exact peak");
}
