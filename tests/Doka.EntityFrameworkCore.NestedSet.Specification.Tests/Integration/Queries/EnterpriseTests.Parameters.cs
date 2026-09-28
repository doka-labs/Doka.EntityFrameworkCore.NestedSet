namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>
    ///     Verifies identical SQL shapes bind different runtime scope and key values as database parameters.
    /// </summary>
    /// <returns>A task that completes after checking parameter values and independent query results.</returns>
    [Fact]
    public async Task RuntimeScopesAndKeysUseParametersWithReusableSqlShape()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await setup.AddRangeAsync(
            [
                new TreeNode { NodeId = 101, Tree = 17, Start = 1, End = 4 },
                new TreeNode { NodeId = 102, Tree = 17, Start = 2, End = 3, Parent = 101, Depth = 1 },
                new TreeNode { NodeId = 202, Tree = 29, Start = 1, End = 4 },
                new TreeNode { NodeId = 203, Tree = 29, Start = 2, End = 3, Parent = 202, Depth = 1 },
            ],
            CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        var firstProbe = new EnterpriseProbe();
        var secondProbe = new EnterpriseProbe();
        await using var firstContext = database.CreateContext((IInterceptor)firstProbe);
        await using var secondContext = database.CreateContext((IInterceptor)secondProbe);
        var first = firstContext
            .NestedSet<TreeNode>()
            .ForScope(17);

        var second = secondContext
            .NestedSet<TreeNode>()
            .ForScope(29);

        // Act
        var results = await Task.WhenAll(
            first
                .ChildrenOf(101)
                .Select(node => node.NodeId)
                .ToArrayAsync(CancellationToken.None),
            second
                .ChildrenOf(202)
                .Select(node => node.NodeId)
                .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal(102, Assert.Single(results[0]));
        Assert.Equal(203, Assert.Single(results[1]));
        Assert.Equal(Assert.Single(firstProbe.Commands), Assert.Single(secondProbe.Commands));
        Assert.Contains(17, Assert.Single(firstProbe.ParameterValues));
        Assert.Contains(101, Assert.Single(firstProbe.ParameterValues));
        Assert.Contains(29, Assert.Single(secondProbe.ParameterValues));
        Assert.Contains(202, Assert.Single(secondProbe.ParameterValues));
    }
}
