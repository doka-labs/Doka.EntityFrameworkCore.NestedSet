namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class CapacityTests
{
    /// <summary>Fully validates and rebuilds a real depth-100000 chain without recursive traversal.</summary>
    /// <returns>A task that completes after persisted deep-chain validation and public rebuild.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task RebuildAsync_DepthOneHundredThousand_ValidatesAndRebuilds()
    {
        // Arrange
        const int count = 100_001;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await WarmAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            count,
            deep: true,
            cancellationToken: TestContext.Current.CancellationToken,
            corrupt: false);

        var tree = setup
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var before = await tree.ValidateAsync(NestedSetValidationLevel.Full, TestContext.Current.CancellationToken);
        await setup
            .Set<TreeNode>()
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(node => node.Start, 1L)
                    .SetProperty(node => node.End, 2L)
                    .SetProperty(node => node.Depth, 9),
                TestContext.Current.CancellationToken);

        var probe = new CapacityProbe();
        await using var context = CreateCapacityContext(database, probe);
        var selected = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        await selected.RebuildAsync(TestContext.Current.CancellationToken);


        var composition = await CompositionCapacityProfiles.MeasureDeepAsync(
            database,
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var consumers = await CompositionConsumerCapacityProfiles.MeasureAsync(
            database,
            Engine,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: true,
            balanced: false);

        // Assert
        CompositionCapacityProfiles.AssertAll(composition, _output);
        CompositionConsumerCapacityProfiles.AssertAll(consumers, _output);
        Assert.True(before.IsValid);
        Assert.Equal(count, before.NodeCount);
        Assert.Equal((count + 63) / 64, probe.CompletedRepairs);
        Assert.Equal(count, probe.RepairedRows);
        Assert.InRange(probe.MaximumRepairParameters, 1, 322);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertCanonicalAsync(
            database,
            count,
            deep: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            100_000,
            await setup
                .Set<TreeNode>()
                .MaxAsync(node => node.Depth, TestContext.Current.CancellationToken));
    }

    /// <summary>Rebuilds one million corrupted nodes through bounded repair commands.</summary>
    /// <returns>A task that completes after public repair and full persisted validation.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task RebuildAsync_MillionNodeTree_RestoresCanonicalGeometry()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await WarmAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        await using var setup = CreateCapacityContext(database);
        await CapacitySeed.SeedAsync(
            setup,
            Million,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false,
            corrupt: false);
        await CompositionCapacityProfiles.BalanceAsync(
            setup,
            Million,
            cancellationToken: TestContext.Current.CancellationToken);

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
        // WHY: Reuse this existing million-row population for balanced read qualification before the repair
        // observer starts. Restore the original wide adjacency while corrupting bounds for the existing repair.
        var composition = await CompositionCapacityProfiles.MeasureAsync(
            database,
            Engine,
            Million,
            balanced: true,
            cancellationToken: TestContext.Current.CancellationToken,
            broad: false);

        var consumers = await CompositionConsumerCapacityProfiles.MeasureAsync(
            database,
            Engine,
            Million,
            cancellationToken: TestContext.Current.CancellationToken,
            balanced: true,
            deep: false);

        var balancedValidation = await setup
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, TestContext.Current.CancellationToken);

        await CompositionCapacityProfiles.CorruptWideForRepairAsync(
            setup,
            Million,
            cancellationToken: TestContext.Current.CancellationToken);

        await tree.RebuildAsync(TestContext.Current.CancellationToken);


        // Assert
        CompositionCapacityProfiles.AssertAll(composition, _output);
        CompositionConsumerCapacityProfiles.AssertAll(consumers, _output);
        Assert.True(balancedValidation.IsValid);
        Assert.Equal(Million, balancedValidation.NodeCount);
        Assert.Equal(15_625, probe.RepairCommands);
        Assert.Equal(15_625, probe.CompletedRepairs);
        Assert.Equal(Million, probe.RepairedRows);
        Assert.InRange(probe.MaximumRepairParameters, 1, 322);
        Assert.InRange(probe.MaximumRepairSqlLength, 1, 20_000);
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertCanonicalAsync(
            database,
            Million,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false);
        Assert.Equal(
            registry with { Revision = registry.Revision + 1 },
            await CapacitySeed.RegistryAsync(
                setup,
                cancellationToken: TestContext.Current.CancellationToken));
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
    public async Task RebuildAsync_MillionNodeLastRepairBatchFailure_RollsBackEarlierBatches(
        int count,
        bool cancel
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
            count,
            corrupt: true,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false);

        var registry = await CapacitySeed.RegistryAsync(
            setup,
            cancellationToken: TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        var probe = new CapacityProbe();
        probe.FailRepairCommand = (count + 63) / 64;
        probe.Cancellation = cancel
            ? cancellation
            : null;

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
        Assert.Equal((count + 63) / 64 - 1, probe.CompletedRepairs);
        Assert.Equal((long)probe.CompletedRepairs * 64, probe.RepairedRows);
        Assert.InRange(probe.MaximumRepairParameters, 1, 322);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = CreateCapacityContext(database);
        Assert.Equal(
            count,
            await verification
                .Set<TreeNode>()
                .LongCountAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            0,
            await CapacitySeed.MismatchesAsync(
                verification,
                count,
                corrupt: true,
                cancellationToken: TestContext.Current.CancellationToken,
                deep: false));

        Assert.Equal(
            registry,
            await CapacitySeed.RegistryAsync(
                verification,
                cancellationToken: TestContext.Current.CancellationToken));

        _output.WriteLine(
            $"Engine={Engine}; nodes={count}; canceled={cancel}; "
            + $"completed repair batches before last failure={probe.CompletedRepairs}; rows and revision restored");
    }
}
