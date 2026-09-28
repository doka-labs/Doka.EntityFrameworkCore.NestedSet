namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Constrains writes to missing identities or database-ranked geometry, rather than every input row.</summary>
public abstract class BulkWriteBudgetTests : ProviderTest
{
    private readonly RelationalFixture _assigned;
    private readonly BulkGeneratedFixture _generated;

    /// <summary>Shares isolated assigned-key and generated-key provider matrices.</summary>
    protected BulkWriteBudgetTests(
        IProviderFixture<RelationalFixture> assigned,
        IProviderFixture<BulkGeneratedFixture> generated
    ) : base(assigned)
    {
        _assigned = assigned.Value;
        _generated = generated.Value;
    }

    /// <summary>
    ///     Generated parent keys cross bounded save batches without retaining the complete tracker.
    /// </summary>
    [Fact]
    public async Task GeneratedImportUsesBoundedBatches()
    {
        // Arrange
        var probe = new StructuralWriteProbe(
            nameof(BulkManualNode),
            nameof(BulkManualNode.Left),
            nameof(BulkManualNode.Right));

        await using var context = await _generated.ResetAsync(Engine, probe);
        var tree = context
            .NestedSet<BulkManualNode>()
            .ForScope(1);

        var saves = 0;
        context.SavedChanges += (_, _) => saves++;
        var children = Enumerable
            .Range(0, 130)
            .Select(_ => new NestedSetBranch<BulkManualNode>(new BulkManualNode()))
            .ToArray();

        var root = new BulkManualNode();

        // Act
        await tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkManualNode, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkManualNode>(root, children)),
            ],
            CancellationToken.None);

        // Assert
        Assert.Equal(3, saves);
        Assert.Single(probe.NodeUpdates);
        Assert.Equal(63, probe.NodeUpdates.Sum(write => write.Rows));
        Assert.All(children, child => Assert.Equal(root.Id, child.Entity.ParentId));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Mapped FK dependencies insert parents first even when numerical keys suggest the reverse.</summary>
    [Fact]
    public async Task AssignedMappedParentsNeedNoFinalizationWrites()
    {
        // Arrange
        var database = await _assigned.ResetAsync(Engine);
        var probe = new StructuralWriteProbe(
            nameof(ConstrainedNode),
            nameof(ConstrainedNode.Left),
            nameof(ConstrainedNode.Right));

        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<ConstrainedNode>()
            .ForScope(1);

        var root = new ConstrainedNode { Id = 300 };
        var child = new ConstrainedNode { Id = 200 };
        var leaf = new ConstrainedNode { Id = 100 };
        var branch = new NestedSetBranch<ConstrainedNode>(
            root,
            [new NestedSetBranch<ConstrainedNode>(child, [new NestedSetBranch<ConstrainedNode>(leaf)])]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<ConstrainedNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        // Assert
        Assert.Empty(probe.NodeUpdates);
        Assert.Equal((1, 6, 0, 0), (root.Left, root.Right, root.Depth, root.Position));
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(child.Id, leaf.ParentId);
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Generated root identities use bounded saves without a parent or coordinate repair pass.</summary>
    [Fact]
    public async Task GeneratedRootsNeedNoFinalizationWrites()
    {
        // Arrange
        var probe = new StructuralWriteProbe(
            nameof(BulkManualNode),
            nameof(BulkManualNode.Left),
            nameof(BulkManualNode.Right));

        await using var context = await _generated.ResetAsync(Engine, probe);
        var tree = context
            .NestedSet<BulkManualNode>()
            .ForScope(1);

        var saves = 0;
        context.SavedChanges += (_, _) => saves++;
        var roots = Enumerable
            .Range(0, 131)
            .Select(_ => new NestedSetBranch<BulkManualNode>(new BulkManualNode()))
            .ToArray();

        var trees = roots
            .Select((branch, index) => new NestedSetTreeImport<BulkManualNode, Guid>(
                index == 0 ? Guid.Empty : Guid.NewGuid(),
                branch))
            .ToArray();

        // Act
        await tree.InsertForestAsync(trees, CancellationToken.None);

        // Assert
        Assert.Equal(3, saves);
        Assert.Empty(probe.NodeUpdates);
        Assert.All(
            roots,
            branch =>
            {
                Assert.True(branch.Entity.Id > 0);
                Assert.Equal(
                    (1L, 2L, 0, 0L),
                    (branch.Entity.Left, branch.Entity.Right, branch.Entity.Depth, branch.Entity.Position));
                Assert.Null(branch.Entity.ParentId);
            });

        Assert.Equal(trees.Select(tree => tree.TreeId), roots.Select(branch => branch.Entity.TreeId));
        Assert.Equal(
            131,
            roots
                .Select(branch => branch.Entity.TreeId)
                .Distinct()
                .Count());
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Only same-batch generated parent links need a bounded finalization write.</summary>
    [Fact]
    public async Task GeneratedChildrenNeedOnlyBatchedParentWrites()
    {
        // Arrange
        var probe = new StructuralWriteProbe(
            nameof(BulkManualNode),
            nameof(BulkManualNode.Left),
            nameof(BulkManualNode.Right));

        await using var context = await _generated.ResetAsync(Engine, probe);
        var tree = context
            .NestedSet<BulkManualNode>()
            .ForScope(1);

        var saves = 0;
        context.SavedChanges += (_, _) => saves++;
        var children = Enumerable
            .Range(0, 130)
            .Select(_ => new NestedSetBranch<BulkManualNode>(new BulkManualNode()))
            .ToArray();

        var root = new BulkManualNode();

        // Act
        await tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkManualNode, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkManualNode>(root, children)),
            ],
            CancellationToken.None);

        // Assert
        Assert.Equal(3, saves);
        Assert.Single(probe.NodeUpdates);
        Assert.Equal(63, probe.NodeUpdates.Sum(write => write.Rows));
        Assert.All(children, child => Assert.Equal(root.Id, child.Entity.ParentId));
        Assert.DoesNotContain(
            probe.Commands,
            command => command.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.Contains("CASE", StringComparison.OrdinalIgnoreCase)
                && command.Contains(nameof(BulkManualNode.Depth), StringComparison.OrdinalIgnoreCase));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Native ordering combines unresolved parents and changed geometry in one bounded update.</summary>
    [Fact]
    public async Task OrderedGeneratedBranchUsesOneFinalizationWrite()
    {
        // Arrange
        var probe = new StructuralWriteProbe(
            "BulkGeneratedNodes",
            nameof(BulkGeneratedNode.Left),
            nameof(BulkGeneratedNode.Right));

        await using var context = await _generated.ResetAsync(Engine, probe);
        var tree = context
            .NestedSet<BulkGeneratedNode>()
            .ForScope(1);

        var root = new BulkGeneratedNode { Name = "Root" };
        var zulu = new BulkGeneratedNode { Name = "Zulu" };
        var alpha = new BulkGeneratedNode { Name = "Alpha" };
        var branch = new NestedSetBranch<BulkGeneratedNode>(
            root,
            [new NestedSetBranch<BulkGeneratedNode>(zulu), new NestedSetBranch<BulkGeneratedNode>(alpha)]);

        // Act
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkGeneratedNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        // Assert
        Assert.Single(probe.NodeUpdates);
        Assert.Equal(3, probe.NodeUpdates.Sum(write => write.Rows));
        Assert.Equal(root.Id, alpha.ParentId);
        Assert.Equal(root.Id, zulu.ParentId);
        Assert.True(alpha.Left < zulu.Left);
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Generated inputs are restored even when insertion needs no finalization UPDATE.</summary>
    [Fact]
    public async Task GeneratedRootRollbackDoesNotDependOnUpdateCount()
    {
        // Arrange
        var failure = new BulkRefreshFailure(nameof(BulkManualNode));
        await using var context = await _generated.ResetAsync(Engine, failure);
        context.SavedChanges += (_, _) => failure.Inserted = true;
        var tree = context
            .NestedSet<BulkManualNode>()
            .ForScope(1);

        var root = new BulkManualNode
        {
            Scope = 17,
            Left = 71,
            Right = 72,
        };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkManualNode, Guid>(Guid.Empty, new NestedSetBranch<BulkManualNode>(root)),],
            CancellationToken.None));

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.True(failure.ReachedRefresh);
        Assert.Equal((0, 17, 71, 72), (root.Id, root.Scope, root.Left, root.Right));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Late failure and retry preserve initially empty caller-owned parent and child navigations.</summary>
    [Fact]
    public async Task NavigationInputsRemainReusableAfterLateFailure()
    {
        // Arrange
        var failure = new BulkRefreshFailure(nameof(BulkNavigationNode));
        await using var context = await _generated.ResetAsync(Engine, failure);
        context.SavedChanges += (_, _) => failure.Inserted = true;
        var tree = context
            .NestedSet<BulkNavigationNode>()
            .ForScope(1);

        var root = new BulkNavigationNode { Id = 20 };
        var child = new BulkNavigationNode { Id = 10 };
        var children = root.Children;
        var branch = new NestedSetBranch<BulkNavigationNode>(root, [new NestedSetBranch<BulkNavigationNode>(child)]);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkNavigationNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None));

        var restored = root.Children.Count == 0 && child.Parent is null && child.ParentId is null;
        await tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkNavigationNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        // Assert
        Assert.IsType<InjectedCommandException>(error);
        Assert.True(restored);
        Assert.Same(children, root.Children);
        Assert.Empty(root.Children);
        Assert.Null(child.Parent);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }
}
