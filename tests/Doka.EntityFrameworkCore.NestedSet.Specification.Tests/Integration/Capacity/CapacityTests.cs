namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies persisted capacity, public bulk atomicity, and bounded repair work on each engine.</summary>

// WHY: Fresh case-owned databases isolate statistics and avoid deleting preceding capacity datasets during setup.
public abstract partial class CapacityTests : ProviderTest
{
    private const int Million = 1_000_000;
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
    public async Task RebuildAsync_NativeSeedAndRepair_PreserveMappedIdentity(
        bool deep
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await WarmAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            129,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: deep,
            corrupt: true);

        var registry = await CapacitySeed.RegistryAsync(
            setup,
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new CapacityProbe();
        await using var context = CreateCapacityContext(database, probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        await tree.RebuildAsync(TestContext.Current.CancellationToken);


        // Assert
        Assert.Equal(3, probe.RepairCommands);
        Assert.Equal(3, probe.CompletedRepairs);
        Assert.Equal(129, probe.RepairedRows);
        await AssertCanonicalAsync(database, 129, cancellationToken: TestContext.Current.CancellationToken, deep: deep);
        Assert.Equal(
            registry with { Revision = registry.Revision + 1 },
            await CapacitySeed.RegistryAsync(
                setup,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Reads every one of ten million persisted nodes and fully validates their stored adjacency.</summary>
    /// <returns>A task that completes after streaming readability and full structural validation.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task AsAsyncEnumerable_TenMillionPersistedNodes_AreReadableAndFullyValid()
    {
        // Arrange
        const int count = 10 * Million;
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
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            readCount++;
            keySum += key;
        }

        var report = await tree.ValidateAsync(NestedSetValidationLevel.Full, TestContext.Current.CancellationToken);
        var composition = await CompositionCapacityProfiles.MeasureAsync(
            database,
            Engine,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            balanced: false,
            broad: false);

        var consumers = await CompositionConsumerCapacityProfiles.MeasureAsync(
            database,
            Engine,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            balanced: false,
            deep: false);

        // Assert
        CompositionCapacityProfiles.AssertAll(composition, _output);
        CompositionConsumerCapacityProfiles.AssertAll(consumers, _output);
        _output.WriteLine($"Engine={Engine}; persisted/read/full-validation nodes={count}; read count={readCount}");
        Assert.Equal(count, readCount);
        Assert.Equal((long)count * (count + 1) / 2, keySum);
        Assert.True(report.IsValid);
        Assert.Equal(count, report.NodeCount);
        Assert.Empty(report.Issues);
        Assert.Equal(
            0,
            await CapacitySeed.MismatchesAsync(
                context,
                count,
                cancellationToken: TestContext.Current.CancellationToken,
                deep: false,
                corrupt: false));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Creates a capacity context without imposing a native transaction undo-latency target.</summary>
    /// <param name="database">The case-owned database.</param>
    /// <param name="probe">The optional command observer for this context.</param>
    /// <returns>An unopened context without SQL or native undo latency gates.</returns>
    private static TreeContext CreateCapacityContext(
        TestDatabase database,
        DbCommandInterceptor? probe = null
    )
    {
        return probe is null
            ? database.CreateContext()
            : database.CreateContext(probe);
    }
}
