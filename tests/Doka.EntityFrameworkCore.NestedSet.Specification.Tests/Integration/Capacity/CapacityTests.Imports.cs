namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class CapacityTests
{
    /// <summary>Atomically imports one million direct children through the public forest API.</summary>
    /// <returns>A task that completes after measured import and independent full persisted validation.</returns>
    [Fact]
    [Trait("Category", "Capacity")]
    public async Task InsertForestAsync_MillionDirectChildren_StayWithinAdditionalHeapBudget()
    {
        // Arrange
        const int count = Million + 1;
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await WarmAsync(database, cancellationToken: TestContext.Current.CancellationToken);
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
            await hierarchy.InsertForestAsync(imports, TestContext.Current.CancellationToken);
        }
        finally
        {
            await probe.StopAsync();
            GC.KeepAlive(imports);
            WriteMeasurement("public forest import", count, probe);
        }

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
        await AssertCanonicalAsync(
            database,
            count,
            cancellationToken: TestContext.Current.CancellationToken,
            deep: false);
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
    public async Task InsertForestAsync_MillionNodeLateImportFailure_RestoresInputsAndRegistry(
        int count,
        bool cancel
    )
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await WarmAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await using var probe = new CapacityProbe();
        probe.FailBulkRefresh = true;
        probe.Cancellation = cancel
            ? cancellation
            : null;

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
                .LongCountAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            0,
            await CapacitySeed.RegistryCountAsync(
                verification,
                cancellationToken: TestContext.Current.CancellationToken));

        _output.WriteLine(
            $"Engine={Engine}; nodes={count}; canceled={cancel}; "
            + $"payload waves before final refresh failure={probe.CompletedSaves}; inputs and reservation restored");
    }
}
