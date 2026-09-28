namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class EnterpriseTests
{
    /// <summary>Verifies one structural read and no hierarchy write lock for native-key validation.</summary>
    /// <returns>A task that completes after query-count and lock-write checks.</returns>
    [Fact]
    public async Task NativeKeyValidationUsesOneStructuralReadWithoutWriteLock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(129));
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var issues = (await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues;

        // Assert
        Assert.Empty(issues);
        Assert.Single(probe.Commands);
        Assert.StartsWith("SELECT", probe.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Verifies public diagnostics identify each corrupt row while isolating another scope.</summary>
    /// <returns>A task that completes after node-specific diagnostic checks.</returns>
    [Fact]
    public async Task DetailedValidationIdentifiesCorruptNodeInSelectedScope()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(3));
        await setup
            .Set<TreeNode>()
            .Where(node => node.NodeId == 2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Depth, 100), CancellationToken.None);

        await setup.AddAsync(
            new TreeNode
            {
                NodeId = 4,
                Tree = 2,
                Start = 1,
                End = 2,
                Depth = 100,
            },
            CancellationToken.None);

        await setup.SavePrecomputedHierarchyAsync(CancellationToken.None);
        await using var context = database.CreateContext();
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        var issues = (await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues;

        // Assert
        var issue = Assert.Single(issues);
        Assert.Equal(2, issue.NodeKey);
        Assert.Equal(NestedSetValidationCode.InvalidDepth, issue.Code);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Verifies flat CASE expressions and mapping-derived parameters in a full repair batch.</summary>
    /// <returns>A task that completes after bounded SQL and native parameter checks.</returns>
    [Fact]
    public async Task RebuildUsesFlatCasesWith322ParametersPerFullBatch()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        await EnterpriseForestTestSupport.SeedForestAsync(setup, EnterpriseForestTestSupport.CreateForest(65), true);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext((IInterceptor)probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        // Act
        await tree.RebuildAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, probe.CompletedNodeUpdates);
        // WHY: Each full batch uses five scalar values per row plus the complete Scope and TreeId identity.
        Assert.Equal(322, probe.MaximumUpdateParameters);
        var update = probe.Commands.First(command => command.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.Contains("TreeNode", StringComparison.Ordinal));

        Assert.Equal(4, update.Split("CASE", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("ELSE CASE", update, StringComparison.OrdinalIgnoreCase);
        await using var verification = database.CreateContext();
        Assert.Equal(Snapshot(EnterpriseForestTestSupport.CreateForest(65)), await SnapshotAsync(verification));
    }
}
