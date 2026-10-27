namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Root, absent, and invisible seeds cannot acquire ancestors through later joins.</summary>
    /// <param name="sourceId">The selected root, hidden leaf, or absent key.</param>
    /// <param name="includeSelf">Whether the first projection includes its own source.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(99, false)]
    [InlineData(99, true)]
    public async Task EmptyOrInvisibleSeedCannotBeResurrected(
        int sourceId,
        bool includeSelf
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 3);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var first = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == sourceId,
                (source, upper, distance) => new { Upper = upper })
            : hierarchy.SelectAncestors(node => node.Id == sourceId,
                (source, upper, distance) => new { Upper = upper });

        var down = hierarchy.SelectSubtrees(
            upper => first.Any(row => row.Upper == upper),
            (upper, node, distance) => new { Upper = upper, Node = node });

        var actual = await first.Join(down, row => row.Upper, row => row.Upper, (left, right) => right.Node.Id)
            .OrderBy(id => id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = sourceId == 1 && includeSelf ? [1, 2, 4, 5, 6] : [];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Hidden ancestors are not returned, and surviving strict ancestors keep their real distance.</summary>
    [Fact]
    public async Task HiddenAncestorDoesNotCompressStrictDistance()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectAncestors(
                node => node.Id == 3,
                (source, upper, distance) => new { Id = upper.Id, Distance = distance })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        var ancestor = Assert.Single(actual);
        Assert.Equal((1, 2), (ancestor.Id, ancestor.Distance));
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>An upward path must orient every application edge from the stored parent to its child.</summary>
    /// <param name="reverseEndpoints">Whether the consumer supplies the invalid lower-to-upper path direction.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpwardPathRequiresUpperToLowerEndpointDirection(
        bool reverseEndpoints
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectAncestors(
            node => node.Id == 3,
            (source, ancestor, distance) => new { Source = source, Ancestor = ancestor });

        // Act
        var query = reverseEndpoints
            ? hierarchy.WherePathEdges(pairs, row => row.Source, row => row.Ancestor,
                (row, parent, child) => child.ParentId == parent.Id)
            : hierarchy.WherePathEdges(pairs, row => row.Ancestor, row => row.Source,
                (row, parent, child) => child.ParentId == parent.Id);

        var actual = await query.Select(row => row.Ancestor.Id).OrderBy(id => id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = reverseEndpoints ? [] : [1, 2];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Radius filters use stored depth when the visible result has a missing intermediate level.</summary>
    /// <param name="radius">The maximum structural distance below the selected anchor.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DescendantRadiusDoesNotCompressInvisibleLevels(
        int radius
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectSubtrees(node => node.Id == 1,
                (anchor, node, distance) => new { Id = node.Id, Distance = distance })
            .Where(row => row.Distance <= radius)
            .OrderBy(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        (int Id, int Distance)[] expected = radius switch
        {
            0 => [(1, 0)],
            1 => [(1, 0), (5, 1)],
            _ => [(1, 0), (3, 2), (4, 2), (5, 1), (6, 2)],
        };

        Assert.Equal(expected, actual.Select(row => (row.Id, row.Distance)));
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>An inclusive expansion of a rejected pivot never restores the previous rejected path.</summary>
    /// <param name="firstAllowed">Whether the earlier source-to-pivot path is accepted.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterSelfExpansionCannotResurrectEarlierBlockedLeg(
        bool firstAllowed
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var first = hierarchy.SelectAncestors(node => node.Id == 3,
                (source, pivot, distance) => new { Source = source, Pivot = pivot })
            .Where(row => row.Pivot.Id == 1);

        // Act
        var allowed = hierarchy.WherePathEdges(first, row => row.Pivot, row => row.Source,
            (row, parent, child) => firstAllowed || child.Id != 2);

        var self = hierarchy.SelectSubtrees(pivot => allowed.Any(row => row.Pivot == pivot),
                (pivot, node, distance) => new { Pivot = pivot, Node = node, Distance = distance })
            .Where(row => row.Distance == 0);

        var actual = await allowed.Join(self, row => row.Pivot, row => row.Pivot,
                (left, right) => right.Node.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = firstAllowed ? [1] : [];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>A hidden closer candidate cannot expose a farther matching owner as a safe nearest result.</summary>
    /// <param name="nearest">Whether complete nearest-path visibility or existential matching is requested.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenCloserCandidateDoesNotExposeFartherMatchingOwner(
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var source = context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 3);

        // Act
        var query = nearest
            ? hierarchy.WhereNearestAncestorOrSelf(source, node => node,
                (node, owner) => owner.Candidate, (node, owner) => owner.Matches)
            : hierarchy.WhereAnyAncestorOrSelf(source, node => node,
                (node, owner) => owner.Candidate && owner.Matches);

        var actual = await query.Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        int[] expected = nearest ? [] : [3];

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Visibility gaps retain raw, existential, and complete-path semantics across a turn.</summary>
    /// <param name="filter">The explicit policy applied to the upward leg of the composed walk.</param>
    /// <param name="accepted">Whether the policy permits visible endpoints across the hidden middle.</param>
    [Theory]
    [InlineData("Raw", true)]
    [InlineData("Any", true)]
    [InlineData("Path", false)]
    [InlineData("Nearest", false)]
    public async Task DirectionChangePreservesExplicitHiddenPathPolicy(
        string filter,
        bool accepted
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, hidden: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var up = hierarchy.SelectAncestors(source => source.Id == 3,
                (source, pivot, distance) => new { Source = source, Pivot = pivot, UpDistance = distance })
            .Where(row => row.Pivot.Id == 1);

        var down = hierarchy.SelectSubtrees(pivot => up.Any(row => row.Pivot == pivot),
                (pivot, target, distance) => new { Pivot = pivot, Target = target, DownDistance = distance })
            .Where(row => row.Target.Id == 6);

        var walks = up.Join(down, row => row.Pivot, row => row.Pivot, (first, second) => new
        {
            first.Source, first.Pivot, second.Target, first.UpDistance, second.DownDistance,
        });

        // Act
        var query = filter switch
        {
            "Any" => hierarchy.WhereAnyAncestorOrSelf(walks, row => row.Source, (row, owner) => owner.Id == 1),
            "Path" => hierarchy.WherePathEdges(walks, row => row.Pivot, row => row.Source,
                (row, parent, child) => true),
            "Nearest" => hierarchy.WhereNearestAncestorOrSelf(walks, row => row.Source,
                (row, owner) => owner.Id == 1, (row, owner) => true),
            _ => walks,
        };

        var actual = await query.Select(row => new
            {
                Source = row.Source.Id, Pivot = row.Pivot.Id, Target = row.Target.Id,
                row.UpDistance, row.DownDistance,
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        (int Source, int Pivot, int Target, int Up, int Down)[] expected = accepted ? [(3, 1, 6, 2, 2)] : [];

        Assert.Equal(expected,
            actual.Select(row => (row.Source, row.Pivot, row.Target, row.UpDistance, row.DownDistance)));

        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }
}
