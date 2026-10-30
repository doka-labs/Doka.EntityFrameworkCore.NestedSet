namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Indexed pairs keep endpoint provenance and visible-edge rules in subsequent generic hierarchy operators.</summary>
    /// <param name="operation">The positive or rejecting operator composed after ancestor expansion.</param>
    /// <param name="capturedFacts">Whether ordinary captured fact selection supplies the same leaf.</param>
    [Theory]
    [InlineData("path", false)]
    [InlineData("blocked-path", false)]
    [InlineData("any", false)]
    [InlineData("missing-any", false)]
    [InlineData("nearest", false)]
    [InlineData("nearest-rejects", false)]
    [InlineData("path", true)]
    [InlineData("blocked-path", true)]
    [InlineData("any", true)]
    [InlineData("missing-any", true)]
    [InlineData("nearest", true)]
    [InlineData("nearest-rejects", true)]
    public async Task IndexedAncestorEndpointsRetainPathAnyAndNearestContracts(string operation, bool capturedFacts)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe);
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var facts = context.Set<CompositionNavigationItem>().Where(fact => fact.Scope == scope && fact.NodeId == 3);
        var pairs = capturedFacts
            ? hierarchy.SelectAncestors(node => facts.Any(fact => fact.NodeId == node.Id),
                (source, owner, distance) => new { Source = source, Owner = owner })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { Source = source, Owner = owner });

        var query = operation switch
        {
            "path" => hierarchy.WherePathEdges(pairs, row => row.Owner, row => row.Source,
                (row, parent, child) => child.ParentId == parent.Id),
            "blocked-path" => hierarchy.WherePathEdges(pairs, row => row.Owner, row => row.Source,
                (row, parent, child) => child.Id != 2),
            "any" => hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Source,
                (row, owner) => owner.Id == 1),
            "missing-any" => hierarchy.WhereAnyAncestorOrSelf(pairs, row => row.Source,
                (row, owner) => owner.Id == 99),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(pairs, row => row.Source,
                (row, owner) => owner.Id < 3, (row, owner) => owner.Id == 2),
            "nearest-rejects" => hierarchy.WhereNearestAncestorOrSelf(pairs, row => row.Source,
                (row, owner) => owner.Id < 3, (row, owner) => owner.Id == 1),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        int[] expected = operation switch
        {
            "blocked-path" => [2],
            "missing-any" or "nearest-rejects" => [],
            _ => [1, 2],
        };

        // WHY: Later path operators have their own aggregates even when the ancestor-source optimization is unavailable.
        probe.Reset();

        // Act
        var actual = await query.OrderBy(row => row.Owner.Id).Select(row => row.Owner.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, pairs);
        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A later direction change retains its indexed first leg with scalar or ordinary captured fact selection.</summary>
    /// <param name="capturedFacts">Whether the selected leaf is reached through an ordinary captured fact Any.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedUpwardSelectionComposesWithDownwardExpansion(bool capturedFacts)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe);
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var facts = context.Set<CompositionNavigationItem>().Where(fact => fact.Scope == scope && fact.NodeId == 3);
        var owners = capturedFacts
            ? hierarchy.SelectAncestors(node => facts.Any(fact => fact.NodeId == node.Id),
                (source, owner, distance) => owner)
            : hierarchy.SelectAncestors(node => node.Id == 3, (source, owner, distance) => owner);

        var query = hierarchy.SelectSubtrees(anchor => owners.Any(owner => owner.Id == anchor.Id),
            (anchor, node, distance) => new { Anchor = anchor, Node = node });

        var guarded = hierarchy.WherePathEdges(query, row => row.Anchor, row => row.Node,
            (row, parent, child) => child.ParentId == parent.Id);

        probe.Reset();

        // Act
        var actual = await guarded.OrderBy(row => row.Anchor.Id).ThenBy(row => row.Node.Id)
            .Select(row => new { Anchor = row.Anchor.Id, Node = row.Node.Id }).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, owners);
        Assert.Equal(new[] { (1, 1), (1, 2), (1, 3), (2, 2), (2, 3) },
            actual.Select(row => (row.Anchor, row.Node)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
