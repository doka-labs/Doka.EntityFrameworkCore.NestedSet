namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTests
{
    /// <summary>
    ///     Verifies strict rebuild restores configured domain order instead of trusting stored positions.
    /// </summary>
    [Fact]
    public async Task StrictRebuildDerivesSiblingOrderFromDomainValues()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        var treeId = await SeedAsync(tree);
        await Nodes(context)
            .Where(node => node.Id == 2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Name, "Zulu"), CancellationToken.None);

        // Act
        await tree
            .InTree(treeId)
            .RebuildAsync(CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(1), 3, 4, 2);
        await AssertIdsAsync(tree.TreeContaining(5), 1, 3, 5, 4, 2);
        await AssertForestAsync(context);
    }

    /// <summary>Verifies flexible rebuild repairs bounds while preserving a deliberate positional override.</summary>
    [Fact]
    public async Task FlexibleRebuildPreservesStoredSiblingOrder()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine, "Flexible");
        var tree = Tree(context);
        var treeId = await SeedAsync(tree);
        await tree.MoveBeforeAsync(4, 2, CancellationToken.None);
        await Nodes(context)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(node => node.Left, 1)
                    .SetProperty(node => node.Right, 2),
                CancellationToken.None);

        // Act
        await tree
            .InTree(treeId)
            .RebuildAsync(CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(1), 4, 2, 3);
        await AssertIdsAsync(tree.TreeContaining(5), 1, 4, 2, 3, 5);
        await AssertForestAsync(context);
    }

    /// <summary>
    ///     Verifies strict validation detects otherwise valid coordinates whose payload order has drifted.
    /// </summary>
    [Fact]
    public async Task StrictValidationReportsDomainOrderDrift()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        var treeId = await SeedAsync(tree);
        await Nodes(context)
            .Where(node => node.Id == 2)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Name, "Zulu"), CancellationToken.None);

        // Act
        var report = await tree
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.False(report.IsValid);
        Assert.NotEmpty(report.Issues);
        await AssertIdsAsync(tree.ChildrenOf(1), 2, 3, 4);
    }

    /// <summary>
    ///     Verifies promoted child subtrees merge into their new sibling group using strict domain order.
    /// </summary>
    [Fact]
    public async Task StrictDeletionSortsPromotedSubtreesAmongExistingSiblings()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Bravo"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(3, "Delta"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(4, "Alpha"), 2, CancellationToken.None);
        await tree.InsertChildAsync(Node(5, "Zulu"), 2, CancellationToken.None);
        await tree.InsertChildAsync(Node(6, "Grandchild"), 4, CancellationToken.None);
        var childWidth = await Nodes(context)
            .Where(node => node.Id == 4)
            .Select(node => node.Right - node.Left + 1)
            .SingleAsync(CancellationToken.None);

        // Act
        await tree.DeleteAsync(2, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(1), 4, 3, 5);
        await AssertIdsAsync(tree.SubtreeOf(4), 4, 6);
        var promoted = await Nodes(context)
            .SingleAsync(node => node.Id == 4, CancellationToken.None);
        Assert.Equal(childWidth, promoted.Right - promoted.Left + 1);
        Assert.Equal(1, promoted.Depth);
        Assert.Equal(
            2,
            await Nodes(context)
                .Where(node => node.Id == 6)
                .Select(node => node.Depth)
                .SingleAsync(CancellationToken.None));
        await AssertForestAsync(context);
    }

    /// <summary>
    ///     Verifies deleting an intermediate node merges its promoted children into the ordered sibling group.
    /// </summary>
    [Fact]
    public async Task StrictIntermediateDeletionSortsPromotedChildren()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = Tree(context);
        await tree.InsertRootAsync(Node(10, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(1, "Middle"), 10, CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Delta"), 10, CancellationToken.None);
        await tree.InsertChildAsync(Node(3, "Alpha"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(4, "Zulu"), 1, CancellationToken.None);

        // Act
        await tree.DeleteAsync(1, CancellationToken.None);

        // Assert
        await AssertIdsAsync(tree.ChildrenOf(10), 3, 2, 4);
        await AssertForestAsync(context);
    }

    /// <summary>Verifies promotion across repair batches preserves every subtree and remains set-based.</summary>
    [Fact]
    public async Task LargePromotionPreservesOverlappingIntervalsAcrossBatches()
    {
        // Arrange
        await using var setup = await _fixture.ResetAsync(Engine);
        var tree = Tree(setup);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Group to remove"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(3, "Member-035a"), 1, CancellationToken.None);
        await tree.InsertRootAsync(Node(4, "Second root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(5, "Unrelated child"), 4, CancellationToken.None);
        var expectedTree = new List<int> { 1 };

        // WHY: More than 64 promoted siblings cross the bounded permutation writer's batch boundary.
        for (var index = 0; index < 70; index++)
        {
            var id = 100 + index;
            await tree.InsertChildAsync(Node(id, $"Member-{index:D3}"), 2, CancellationToken.None);
            expectedTree.Add(id);

            if (index % 10 == 0)
            {
                var grandchild = 1000 + index;
                await tree.InsertChildAsync(Node(grandchild, "Grandchild"), id, CancellationToken.None);
                expectedTree.Add(grandchild);
            }

            if (index == 35)
            {
                expectedTree.Add(3);
            }
        }

        var other = Tree(setup, 2);
        await other.InsertRootAsync(Node(9000, "Root"), Guid.NewGuid(), CancellationToken.None);
        await other.InsertChildAsync(Node(9001, "Member-000"), 9000, CancellationToken.None);
        var otherBefore = await Nodes(setup, 2)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var subtreeWidths = await Nodes(setup)
            .Where(node => node.ParentId == 2)
            .ToDictionaryAsync(node => node.Id, node => node.Right - node.Left + 1, CancellationToken.None);

        var probe = new OrderingSaveProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, "Strict", probe);

        // Act
        await Tree(context).DeleteAsync(2, CancellationToken.None);
        var structuralCommands = probe.StructuralUpdates;

        // Assert
        Assert.InRange(structuralCommands, 1, 29);
        Assert.Equal(
            expectedTree,
            await IdsAsync(Tree(context).TreeContaining(1000)));
        await AssertIdsAsync(Tree(context).TreeContaining(5), 4, 5);
        AssertSnapshot(
            otherBefore,
            await Nodes(context, 2)
                .OrderBy(node => node.Id)
                .ToArrayAsync(CancellationToken.None));

        var promoted = await Tree(context)
            .ChildrenOf(1)
            .ToArrayAsync(CancellationToken.None);

        foreach (var node in promoted.Where(node => node.Id >= 100))
        {
            Assert.Equal(subtreeWidths[node.Id], node.Right - node.Left + 1);
        }

        await AssertForestAsync(context);
        await AssertForestAsync(context, 2);
    }

    /// <summary>
    ///     Preserves a pending ordering edit during exact-tree inspection inside a caller-owned transaction.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactTreeInspectionPreservesPendingOrderingChanges(
        bool planRebuild
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var treeId = await SeedAsync(Tree(context));
        var renamed = await context
            .Set<OrderingNode>()
            .SingleAsync(node => node.Id == 2, CancellationToken.None);

        var name = context
            .Entry(renamed)
            .Property(node => node.Name);

        name.CurrentValue = "Zulu";
        name.IsModified = true;
        var originalGeometry = (renamed.TreeId, renamed.Left, renamed.Right, renamed.Depth, renamed.Position);
        await using var transaction = await BeginAsync(context);
        var tree = Tree(context)
            .InTree(treeId);

        // Act
        var failure = await Record.ExceptionAsync(async () =>
        {
            if (planRebuild)
            {
                await tree.PlanRebuildAsync(CancellationToken.None);
            }
            else
            {
                await tree.ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);
            }
        });

        // Assert
        Assert.Null(failure);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        Assert.Equal("Alpha", name.OriginalValue);
        Assert.Equal("Zulu", name.CurrentValue);
        Assert.True(name.IsModified);
        Assert.Equal(originalGeometry, (renamed.TreeId, renamed.Left, renamed.Right, renamed.Depth, renamed.Position));
        Assert.Equal(
            "Alpha",
            await tree
                .Nodes
                .Where(node => node.Id == 2)
                .Select(node => node.Name)
                .SingleAsync(CancellationToken.None));
        await transaction.RollbackAsync(CancellationToken.None);
    }
}
