namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AutomaticMoveBudgetTests
{
    /// <summary>
    ///     Locked source and anchor validation rejects unsafe automatic moves before native ranking or writes.
    /// </summary>
    [Theory]
    [InlineData("missing-source", NestedSetErrorCode.NodeNotFound)]
    [InlineData("missing-anchor", NestedSetErrorCode.NodeNotFound)]
    [InlineData("source-bounds", NestedSetErrorCode.InvalidStructure)]
    [InlineData("anchor-bounds", NestedSetErrorCode.InvalidStructure)]
    [InlineData("self", NestedSetErrorCode.CycleDetected)]
    [InlineData("descendant", NestedSetErrorCode.CycleDetected)]
    public async Task AutomaticPlacementRetainsLockedValidation(
        string scenario,
        NestedSetErrorCode expected
    )
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        await SeedAsync(setup);

        if (scenario is "source-bounds" or "anchor-bounds")
        {
            var corruptedKey = scenario == "source-bounds" ? 3 : 2;
            await setup
                .Set<OrderingNode>()
                .Where(node => node.Id == corruptedKey)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.Right, node => node.Left + 2),
                    CancellationToken.None);
        }

        var before = await SnapshotAsync(setup);
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);
        var tree = context
            .NestedSet<OrderingNode>()
            .ForScope(1);

        var source = scenario == "missing-source" ? 999 : 3;
        var target = scenario switch
        {
            "missing-anchor" => 999,
            "self" => 3,
            "descendant" => 4,
            _ => 2,
        };

        // Act
        var error = await Record.ExceptionAsync(() => tree.MoveToAsync(source, target, CancellationToken.None));

        // Assert
        Assert.Equal(expected, Assert.IsType<NestedSetException>(error).Code);
        if (expected == NestedSetErrorCode.NodeNotFound)
        {
            // WHY: An absent public anchor cannot identify a tree to lock; resolution rejects it before mutation.
            Assert.Single(FacadeIdentityReads(probe, "Strict", context));
            Assert.DoesNotContain(probe.Commands, sql => NestedSetTestInfrastructure.ReferencesRegistry(context, sql));
        }
        else
        {
            Assert.Single(FacadeIdentityReads(probe, "Strict", context));
            Assert.Contains(probe.Commands, sql => NestedSetTestInfrastructure.ReferencesRegistry(context, sql));
        }

        Assert.DoesNotContain(
            LockedHierarchyReads(probe, "Strict", context),
            sql => sql.Contains("LAG(", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(probe.Commands, IsHierarchyWrite);
        Assert.Equal(before, await SnapshotAsync(setup));
    }
}
