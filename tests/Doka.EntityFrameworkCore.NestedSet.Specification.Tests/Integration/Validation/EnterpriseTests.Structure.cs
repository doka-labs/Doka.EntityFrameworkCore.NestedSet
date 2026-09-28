namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Verifies that structural operations never materialize or select application payload.</summary>
    /// <param name="operation">The single structural operation to observe.</param>
    /// <returns>A task that completes after the materialization and SQL projection checks.</returns>
    [Theory]
    [InlineData("Move")]
    [InlineData("Validate")]
    [InlineData("Rebuild")]
    public async Task StructuralOperationsDoNotReadPayload(
        string operation
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3));
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await ApplyStructuralOperationAsync(tree, operation);

        // Assert
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.DoesNotContain(
            probe.Commands,
            command => command.Contains("Payload", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(probe.Commands);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = database.CreateContext();
        Assert.All(
            await verification
                .Set<TreeNode>()
                .ToListAsync(CancellationToken.None),
            node => Assert.Equal(4096, node.Payload!.Length));
    }

    /// <summary>Verifies that an already valid forest incurs no hierarchy UPDATE commands.</summary>
    /// <returns>A task that completes after command and persisted-state checks.</returns>
    [Fact]
    public async Task ValidRebuildDoesNotWriteNodes()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var expected = EnterpriseForestTestSupport.CreateForest(129);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, expected);
        var registryBefore = await RegistryStateAsync(setup);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, probe.NodeUpdates);
        Assert.Equal(0, probe.MaterializedNodes);
        await using var verification = database.CreateContext();
        Assert.Equal(Snapshot(expected), await SnapshotAsync(verification));
        Assert.Equal(
            registryBefore with { Revision = registryBefore.Revision + 1 },
            await RegistryStateAsync(verification));
    }

    /// <summary>Verifies bounded repair batches at the first size requiring a third batch.</summary>
    /// <returns>A task that completes after repair correctness and SQL-size checks.</returns>
    [Fact]
    public async Task RebuildRepairs129NodesInThreeBoundedCommands()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var expected = EnterpriseForestTestSupport.CreateForest(129);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, expected, true);
        var registryBefore = await RegistryStateAsync(setup);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        await tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, probe.NodeUpdates);
        Assert.Equal(3, probe.CompletedNodeUpdates);
        Assert.InRange(probe.MaximumUpdateParameters, 1, 577);
        Assert.Equal(0, probe.MaterializedNodes);
        await using var verification = database.CreateContext();
        Assert.Equal(Snapshot(expected), await SnapshotAsync(verification));
        Assert.Equal(
            registryBefore with { Revision = registryBefore.Revision + 1 },
            await RegistryStateAsync(verification));
    }

    /// <summary>Verifies that a second-batch failure rolls back the repair already persisted by batch one.</summary>
    /// <returns>A task that completes after checking the original corrupted snapshot.</returns>
    [Fact]
    public async Task RepairFailureRollsBackCompletedFirstBatch()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(129), true);
        var before = await SnapshotAsync(setup);
        var registryBefore = await RegistryStateAsync(setup);
        var probe = new EnterpriseProbe { FailSecondNodeUpdate = true };
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree
            .InTree(Guid.Empty)
            .RebuildAsync(CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(exception);
        Assert.Equal(1, probe.CompletedNodeUpdates);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
        Assert.Equal(registryBefore, await RegistryStateAsync(verification));
    }

    /// <summary>Verifies that cancellation at a repair boundary rolls back an executed earlier batch.</summary>
    /// <returns>A task that completes after checking atomic cancellation.</returns>
    [Fact]
    public async Task RepairCancellationRollsBackCompletedFirstBatch()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(129), true);
        var before = await SnapshotAsync(setup);
        var registryBefore = await RegistryStateAsync(setup);
        using var cancellation = new CancellationTokenSource();
        var probe = new EnterpriseProbe { CancelSecondNodeUpdate = cancellation };
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => tree
            .InTree(Guid.Empty)
            .RebuildAsync(cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(1, probe.CompletedNodeUpdates);
        Assert.Null(context.Database.CurrentTransaction);
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
        Assert.Equal(registryBefore, await RegistryStateAsync(verification));
    }

    /// <summary>Verifies valid wide and deep forests without recursive traversal or entity materialization.</summary>
    /// <param name="deep">Whether the forest is a chain instead of one wide sibling group.</param>
    /// <returns>A task that completes after checking the validation result.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidationHandlesWideAndDeepForests(
        bool deep
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(2049, deep));
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var errors = (await tree
            .InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues;

        // Assert
        Assert.Empty(errors);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Equal(0, probe.NodeUpdates);
        Assert.DoesNotContain(
            probe.Commands,
            command => command.Contains("Payload", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies the shared observer counts real entities while isolating another context's counters.</summary>
    /// <returns>A task that completes after checking the materialization observer's positive control.</returns>
    [Fact]
    public async Task MaterializationObserverCountsEntitiesOnlyForTheirOwningContext()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3));
        var firstProbe = new EnterpriseProbe();
        var secondProbe = new EnterpriseProbe();
        await using var first = database.CreateContext((IInterceptor)firstProbe);
        await using var second = database.CreateContext((IInterceptor)secondProbe);

        // Act
        var materialized = await first
            .Set<TreeNode>()
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);

        var projectedCount = await second
            .Set<TreeNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, materialized.Count);
        Assert.Equal(3, projectedCount);
        Assert.Equal(3, firstProbe.MaterializedNodes);
        Assert.Equal(0, secondProbe.MaterializedNodes);
    }

    /// <summary>Dispatches exactly one structural operation for projection observation.</summary>
    private static async Task ApplyStructuralOperationAsync(
        ScopedNestedSet<TreeNode, int> tree,
        string operation
    )
    {
        switch (operation)
        {
            case "Move":
                await tree.MoveToAsync(3, 2, CancellationToken.None);
                break;
            case "Validate":
                await tree
                    .InTree(Guid.Empty)
                    .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
                break;
            case "Rebuild":
                await tree
                    .InTree(Guid.Empty)
                    .RebuildAsync(CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>Reads the selected tree's persisted registry revision and lifecycle by mapped identity.</summary>
    private static Task<RegistryState> RegistryStateAsync(
        TreeContext context
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(TreeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy)
            .Registry;

        return context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Where(row => EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope) == 1
                && EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == Guid.Empty)
            .Select(row => new RegistryState(
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .SingleAsync(CancellationToken.None);
    }

    /// <summary>Stores the complete mutable registry state for equality and rollback assertions.</summary>
    private sealed record RegistryState(
        long Revision,
        byte Lifecycle
    );
}
