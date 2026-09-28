namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>A valid tree produces an empty rebuild plan without changing rows or registry state.</summary>
    /// <returns>A task that completes after the dry-run and persisted-state checks.</returns>
    [Fact]
    public async Task ValidTreePlanHasNoRepairsOrWrites()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3));
        var before = await SnapshotAsync(setup);
        var registryBefore = await RegistryStateAsync(setup);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var plan = await tree.PlanRebuildAsync(CancellationToken.None);

        // Assert
        Assert.True(plan.CanRebuild);
        Assert.Equal(3, plan.NodeCount);
        Assert.Equal(0, plan.ChangedNodeCount);
        Assert.Equal(0, plan.BatchCount);
        Assert.Empty(plan.AffectedRoles);
        Assert.Empty(plan.Issues);
        Assert.Equal(0, probe.NodeUpdates);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
        Assert.Equal(registryBefore, await RegistryStateAsync(verification));
    }

    /// <summary>A repairable tree reports the exact changed nodes, write batch, roles, and typed issues.</summary>
    /// <returns>A task that completes after the dry-run and persisted-state checks.</returns>
    [Fact]
    public async Task RepairableTreePlanReportsExactWorkWithoutWrites()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3), true);
        var before = await SnapshotAsync(setup);
        var registryBefore = await RegistryStateAsync(setup);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var plan = await tree.PlanRebuildAsync(CancellationToken.None);

        // Assert
        Assert.True(plan.CanRebuild);
        Assert.Equal(3, plan.NodeCount);
        Assert.Equal(3, plan.ChangedNodeCount);
        Assert.Equal(1, plan.BatchCount);
        AssertAllRebuildRoles(plan.AffectedRoles);
        Assert.Equal(5, plan.Issues.Count);
        Assert.Equal(3, plan.Issues.Count(issue => issue.Code == NestedSetValidationCode.InvalidBounds));
        Assert.Equal(2, plan.Issues.Count(issue => issue.Code == NestedSetValidationCode.InvalidDepth));
        Assert.Contains(plan.Issues, issue => issue is { Code: NestedSetValidationCode.InvalidBounds, NodeKey: 1 });
        Assert.Contains(plan.Issues, issue => issue is { Code: NestedSetValidationCode.InvalidDepth, NodeKey: 2 });
        Assert.Contains(plan.Issues, issue => issue is { Code: NestedSetValidationCode.InvalidDepth, NodeKey: 3 });
        Assert.Equal(0, probe.NodeUpdates);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
        Assert.Equal(registryBefore, await RegistryStateAsync(verification));
    }

    /// <summary>A parent cycle is reported without changing tree rows or registry state.</summary>
    /// <returns>A task that completes after typed issue and persisted-state checks.</returns>
    [Fact]
    public async Task ParentCyclePlanReportsUnrepairableStructureWithoutWrites()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3));
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Parent, 2), CancellationToken.None);

        var before = await SnapshotAsync(setup);
        var registryBefore = await RegistryStateAsync(setup);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var plan = await tree.PlanRebuildAsync(CancellationToken.None);

        // Assert
        Assert.False(plan.CanRebuild);
        Assert.Equal(3, plan.NodeCount);
        Assert.Equal(0, plan.ChangedNodeCount);
        Assert.Equal(0, plan.BatchCount);
        Assert.Empty(plan.AffectedRoles);
        Assert.Equal(4, plan.Issues.Count);
        Assert.Contains(
            plan.Issues,
            issue => issue is { Code: NestedSetValidationCode.InvalidRootCount, NodeKey: null });
        Assert.Equal(3, plan.Issues.Count(issue => issue.Code == NestedSetValidationCode.CycleOrUnreachableNode));
        Assert.Contains(
            plan.Issues,
            issue => issue is { Code: NestedSetValidationCode.CycleOrUnreachableNode, NodeKey: 1 });
        Assert.Contains(
            plan.Issues,
            issue => issue is { Code: NestedSetValidationCode.CycleOrUnreachableNode, NodeKey: 2 });
        Assert.Contains(
            plan.Issues,
            issue => issue is { Code: NestedSetValidationCode.CycleOrUnreachableNode, NodeKey: 3 });
        Assert.Equal(0, probe.NodeUpdates);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = database.CreateContext();
        Assert.Equal(before, await SnapshotAsync(verification));
        Assert.Equal(registryBefore, await RegistryStateAsync(verification));
    }

    /// <summary>Asserts the documented roles in their stable public order.</summary>
    private static void AssertAllRebuildRoles(
        IReadOnlyList<NestedSetStructuralRole> roles
    ) => Assert.Collection(
        roles,
        role => Assert.Equal(NestedSetStructuralRole.Left, role),
        role => Assert.Equal(NestedSetStructuralRole.Right, role),
        role => Assert.Equal(NestedSetStructuralRole.Depth, role),
        role => Assert.Equal(NestedSetStructuralRole.Position, role));
}
