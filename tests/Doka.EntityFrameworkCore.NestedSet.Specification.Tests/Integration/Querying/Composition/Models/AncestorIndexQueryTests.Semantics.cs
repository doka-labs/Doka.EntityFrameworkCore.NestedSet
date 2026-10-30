namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Retains exact scope/tree membership and ordinary query-filter behavior around the indexed fence.</summary>
    /// <param name="includeSelf">Whether the selected node is an eligible equal-bound owner.</param>
    /// <param name="shape">A visible, hidden, missing or empty source/owner configuration.</param>
    [Theory]
    [InlineData(false, "visible")]
    [InlineData(true, "visible")]
    [InlineData(false, "hidden-source")]
    [InlineData(true, "hidden-source")]
    [InlineData(false, "hidden-owner")]
    [InlineData(true, "hidden-owner")]
    [InlineData(false, "missing-source")]
    [InlineData(true, "missing-source")]
    [InlineData(false, "empty-selection")]
    [InlineData(true, "empty-selection")]
    public async Task IndexedAncestorsKeepScopeTreeVisibilityAndEmptySelection(bool includeSelf, string shape)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe, foreignTrees: true);

        if (shape is "hidden-source" or "hidden-owner")
        {
            var hidden = shape == "hidden-source" ? 3 : 2;
            await context.Set<CompositionNavigationNode>().Where(node => node.Scope == scope && node.Id == hidden)
                .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Visible, false), CancellationToken.None);
        }

        var selected = shape == "missing-source" ? 99 : shape == "empty-selection" ? -1 : 3;
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope);
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == selected,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, owner.Scope, owner.TreeId })
            : hierarchy.SelectAncestors(node => node.Id == selected,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, owner.Scope, owner.TreeId });

        int[] expected = shape switch
        {
            "hidden-source" or "missing-source" or "empty-selection" => [],
            "hidden-owner" => includeSelf ? [1, 3] : [1],
            _ => includeSelf ? [1, 2, 3] : [1, 2],
        };

        probe.Reset();

        // Act
        var actual = await query.OrderBy(row => row.Owner).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual.Select(row => row.Owner));
        Assert.All(actual, row =>
        {
            Assert.Equal(3, row.Source);
            Assert.Equal(scope, row.Scope);
            Assert.Equal(tree, row.TreeId);
        });

        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Inclusive bounds include different keys and shared boundaries without weakening strict containment.</summary>
    /// <param name="includeSelf">Whether equal/shared boundaries are admitted.</param>
    /// <param name="shape">The independently created owner interval.</param>
    [Theory]
    [InlineData(false, "equal-leaf")]
    [InlineData(true, "equal-leaf")]
    [InlineData(false, "equal-nonleaf")]
    [InlineData(true, "equal-nonleaf")]
    [InlineData(false, "shared-left")]
    [InlineData(true, "shared-left")]
    [InlineData(false, "shared-right")]
    [InlineData(true, "shared-right")]
    public async Task IndexedAncestorsPreserveEqualAndSharedBounds(bool includeSelf, string shape)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe);
        var source = shape == "equal-nonleaf" ? 2 : 3;
        var owner = CreateNode(scope, tree, 4);
        owner.ParentId = null;
        owner.Tags.Clear();
        owner.Items.Clear();
        owner.Left = shape is "equal-nonleaf" or "shared-right" ? 2 : 3;
        owner.Right = shape is "equal-nonleaf" or "shared-left" ? 5 : 4;
        await context.AddAsync(owner, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == source, (node, ancestor, distance) => ancestor.Id)
            : hierarchy.SelectAncestors(node => node.Id == source, (node, ancestor, distance) => ancestor.Id);

        int[] expected = source == 2
            ? includeSelf ? [1, 2, 4] : [1]
            : includeSelf ? [1, 2, 3, 4] : [1, 2];

        probe.Reset();

        // Act
        var actual = await query.OrderBy(key => key).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Late consumer joins retain duplicate occurrences and equal projected values.</summary>
    /// <param name="includeSelf">Whether each application row receives a source/self occurrence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedAncestorsRetainConsumerOccurrenceBags(bool includeSelf)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe);
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var pairs = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id });

        var query = pairs.Join(context.Set<CompositionNavigationItem>().Where(item => item.Scope == scope),
            pair => pair.Source, item => item.NodeId, (pair, item) => 0);

        probe.Reset();

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(includeSelf ? 6 : 4, actual.Length);
        Assert.All(actual, value => Assert.Equal(0, value));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
