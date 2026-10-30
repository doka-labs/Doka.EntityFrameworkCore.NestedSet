namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Entity projection preserves owned auto-includes and each overlapping anchor occurrence.</summary>
    /// <param name="composer">Whether the library or the original relational range join builds the pairs.</param>
    /// <param name="selection">Visible nodes, a filtered leaf, or an absent requested leaf.</param>
    [Theory]
    [InlineData(false, "visible")]
    [InlineData(true, "visible")]
    [InlineData(false, "hidden-leaf")]
    [InlineData(true, "hidden-leaf")]
    [InlineData(false, "missing-leaf")]
    [InlineData(true, "missing-leaf")]
    public async Task SubtreeEntityProjectionPreservesOwnedNavigations(
        bool composer,
        string selection
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await CreateNavigationContextAsync(scope, tree, selection, probe, entity =>
        {
            if (entity is CompositionNavigationNode)
            {
                materialized++;
            }
        });

        materialized = 0;
        var nodes = context.Set<CompositionNavigationNode>().Where(node => node.Scope == scope && node.TreeId == tree);
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var expected = NavigationPairs(selection).Select(pair => pair.Node).Order().ToArray();

        // Act
        var query = composer
            ? hierarchy.SelectSubtrees(anchor => anchor.Id <= 2, (anchor, node, distance) => node)
            : nodes.Where(anchor => anchor.Id <= 2).SelectMany(anchor => nodes.Where(node =>
                node.Left >= anchor.Left && node.Right <= anchor.Right));

        if (selection == "missing-leaf")
        {
            query = query.Where(node => node.Id == 99);
        }

        var actual = await query.AsNoTracking().OrderBy(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(node => node.Id));
        Assert.All(actual, node =>
        {
            Assert.Equal($"details-{node.Id}", node.Details.Caption);
            Assert.Equal(new[] { $"tag-{node.Id}-1", $"tag-{node.Id}-2" },
                node.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value));
        });

        Assert.Equal(actual.Length, materialized);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Navigation DTOs retain relational identifiers for owned and ordinary collection shaping.</summary>
    /// <param name="composer">Whether the library or the original relational range join builds the pairs.</param>
    /// <param name="selection">Visible nodes, a filtered leaf, or an absent requested leaf.</param>
    [Theory]
    [InlineData(false, "visible")]
    [InlineData(true, "visible")]
    [InlineData(false, "hidden-leaf")]
    [InlineData(true, "hidden-leaf")]
    [InlineData(false, "missing-leaf")]
    [InlineData(true, "missing-leaf")]
    public async Task SubtreeProjectionPreservesOwnedAndOrdinaryCollectionDtos(
        bool composer,
        string selection
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await CreateNavigationContextAsync(
            scope, tree, selection, probe, _ => materialized++);

        materialized = 0;
        var nodes = context.Set<CompositionNavigationNode>().Where(node => node.Scope == scope && node.TreeId == tree);
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var expected = NavigationPairs(selection);

        // Act
        var pairs = composer
            ? hierarchy.SelectSubtrees(anchor => anchor.Id <= 2,
                (anchor, node, distance) => new { Anchor = anchor, Node = node })
            : nodes.Where(anchor => anchor.Id <= 2).SelectMany(anchor => nodes.Where(node =>
                    node.Left >= anchor.Left && node.Right <= anchor.Right),
                (anchor, node) => new { Anchor = anchor, Node = node });

        if (selection == "missing-leaf")
        {
            pairs = pairs.Where(row => row.Node.Id == 99);
        }

        // WHY: These tiny fixtures intentionally shape both endpoints' collections in one SQL statement.
        // Explicit single-query mode keeps the command assertion independent of application split-query defaults.
        var actual = await pairs.AsSingleQuery().OrderBy(row => row.Anchor.Id).ThenBy(row => row.Node.Id)
            .Select(row => new
            {
                Anchor = row.Anchor.Id,
                Node = row.Node.Id,
                row.Node.Details.Caption,
                AnchorTags = row.Anchor.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
                AnchorItems = row.Anchor.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
                Tags = row.Node.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
                Items = row.Node.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
            }).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(row => (row.Anchor, row.Node)));
        Assert.All(actual, row =>
        {
            Assert.Equal($"details-{row.Node}", row.Caption);
            Assert.Equal(new[] { $"tag-{row.Anchor}-1", $"tag-{row.Anchor}-2" }, row.AnchorTags);
            Assert.Equal(new[] { $"item-{row.Anchor}-1", $"item-{row.Anchor}-2" }, row.AnchorItems);
            Assert.Equal(new[] { $"tag-{row.Node}-1", $"tag-{row.Node}-2" }, row.Tags);
            Assert.Equal(new[] { $"item-{row.Node}-1", $"item-{row.Node}-2" }, row.Items);
        });

        Assert.Equal(0, materialized);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Seeds complete navigation identities in one isolated application scope.</summary>
    private async Task<CompositionNavigationContext> CreateNavigationContextAsync(
        Guid scope,
        Guid tree,
        string selection,
        CompositionCommandProbe probe,
        Action<object> materialized
    )
    {
        var context = await _fixture.CreateContextAsync<CompositionNavigationContext>(Engine,
            static options => new CompositionNavigationContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, materialized);

        try
        {
            for (var id = 1; id <= 3; id++)
            {
                var node = new CompositionNavigationNode
                {
                    Scope = scope,
                    TreeId = tree,
                    Id = id,
                    ParentId = id == 1 ? null : id - 1,
                    Left = id,
                    Right = 7 - id,
                    Depth = id - 1,
                    Visible = selection != "hidden-leaf" || id != 3,
                    Details = new CompositionNavigationDetail { Caption = $"details-{id}" },
                };

                for (var item = 1; item <= 2; item++)
                {
                    node.Tags.Add(new CompositionNavigationTag
                    {
                        Id = item,
                        Value = $"tag-{id}-{item}",
                    });

                    node.Items.Add(new CompositionNavigationItem
                    {
                        Scope = scope,
                        Id = (id * 10) + item,
                        NodeId = id,
                        Label = $"item-{id}-{item}",
                    });
                }

                await context.AddAsync(node, CancellationToken.None);
            }

            await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Enumerates expected anchor occurrences independently of bounds or SQL result shaping.</summary>
    private static (int Anchor, int Node)[] NavigationPairs(string selection)
        => selection switch
        {
            "visible" => [(1, 1), (1, 2), (1, 3), (2, 2), (2, 3)],
            "hidden-leaf" => [(1, 1), (1, 2), (2, 2)],
            "missing-leaf" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
}
