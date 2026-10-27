namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>Direction changes retain each source occurrence and both source-relative distances.</summary>
    [Fact]
    public async Task UpThenDownPreservesPerSourceBagAndDistances()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var up = hierarchy.SelectAncestorsOrSelf(
                node => node.Id == 3 || node.Id == 4,
                (node, pivot, distance) => new { Source = node, Pivot = pivot, UpDistance = distance })
            .Where(row => row.Pivot.Id == 2);

        var down = hierarchy.SelectSubtrees(
            pivot => up.Any(row => row.Pivot == pivot),
            (pivot, node, distance) => new { Pivot = pivot, Target = node, DownDistance = distance });

        var rows = up.Join(
            down,
            row => row.Pivot,
            row => row.Pivot,
            (first, second) => new
            {
                first.Source,
                first.Pivot,
                second.Target,
                first.UpDistance,
                second.DownDistance,
            });

        var allowedUp = hierarchy.WherePathEdges(
            rows,
            row => row.Pivot,
            row => row.Source,
            (row, parent, child) => child.EdgeOpen);

        var query = hierarchy.WherePathEdges(
                allowedUp,
                row => row.Pivot,
                row => row.Target,
                (row, parent, child) => child.EdgeOpen)
            .Select(row => new
            {
                Source = row.Source.Id,
                Target = row.Target.Id,
                row.UpDistance,
                row.DownDistance,
            })
            .OrderBy(row => row.Source)
            .ThenBy(row => row.Target);

        var commandsBeforeExecution = probe.CommandCount;
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, commandsBeforeExecution);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(
            [(3, 2, 1, 0), (3, 3, 1, 1), (3, 4, 1, 1), (4, 2, 1, 0), (4, 3, 1, 1), (4, 4, 1, 1)],
            actual.Select(row => (row.Source, row.Target, row.UpDistance, row.DownDistance)));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An open destination branch cannot bypass a rejected first leg through a shared pivot.</summary>
    [Fact]
    public async Task ClosedUpwardLegCannotReappearThroughOpenDownwardLeg()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe, closed: 2);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var up = hierarchy.SelectAncestors(
                source => source.Id == 3,
                (source, pivot, distance) => new { Source = source, Pivot = pivot })
            .Where(row => row.Pivot.Id == 1);

        var allowedUp = hierarchy.WherePathEdges(
            up,
            row => row.Pivot,
            row => row.Source,
            (row, parent, child) => child.EdgeOpen);

        var down = hierarchy.SelectSubtrees(
            pivot => allowedUp.Any(row => row.Pivot == pivot),
            (pivot, node, distance) => new { Pivot = pivot, Target = node });

        var rows = allowedUp.Join(
                down,
                row => row.Pivot,
                row => row.Pivot,
                (first, second) => new { first.Source, first.Pivot, second.Target })
            .Where(row => row.Target.Id == 6);

        var actual = await hierarchy.WherePathEdges(
                rows,
                row => row.Pivot,
                row => row.Target,
                (row, parent, child) => child.EdgeOpen)
            .Select(row => row.Target.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>A reverse search may rise above its application anchor unless that boundary is explicit.</summary>
    /// <param name="fenceOrigin">Whether the reverse leg must stop at the original subtree anchor.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownThenUpPreservesTurnMultiplicityAndExplicitOriginFence(
        bool fenceOrigin
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);

        // Act
        var down = hierarchy.SelectSubtrees(
            origin => origin.Id == 2,
            (origin, turn, distance) => new { Origin = origin, Turn = turn, DownDistance = distance });

        var up = hierarchy.SelectAncestorsOrSelf(
            turn => down.Any(row => row.Turn == turn),
            (turn, upper, distance) => new { Turn = turn, Upper = upper, UpDistance = distance });

        var rows = down.Join(
            up,
            row => row.Turn,
            row => row.Turn,
            (first, second) => new
            {
                first.Origin,
                first.Turn,
                second.Upper,
                first.DownDistance,
                second.UpDistance,
            });

        if (fenceOrigin)
        {
            rows = rows.Where(row => row.UpDistance <= row.DownDistance);
        }

        var actual = await rows.OrderBy(row => row.Turn.Id)
            .ThenBy(row => row.Upper.Id)
            .Select(row => new { Turn = row.Turn.Id, Upper = row.Upper.Id, row.UpDistance })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        (int Turn, int Upper, int Distance)[] expected = fenceOrigin
            ? [(2, 2, 0), (3, 2, 1), (3, 3, 0), (4, 2, 1), (4, 4, 0)]
            : [(2, 1, 1), (2, 2, 0), (3, 1, 2), (3, 2, 1), (3, 3, 0), (4, 1, 2), (4, 2, 1), (4, 4, 0)];

        Assert.Equal(expected, actual.Select(row => (row.Turn, row.Upper, row.UpDistance)));
        Assert.Equal(1, probe.CommandCount);
    }

    /// <summary>Owner filters preserve outer occurrences without multiplying them by matching ancestors.</summary>
    /// <param name="nearest">Whether selection uses the nearest owner rather than an existential match.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerFiltersPreserveDuplicateOuterRowsWithoutMultiplyingThem(
        bool nearest
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var occurrence = context.Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id == 3)
            .Select(node => new { Node = node });

        var source = occurrence.Concat(occurrence);

        // Act
        var query = nearest
            ? hierarchy.WhereNearestAncestorOrSelf(
                source,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => true)
            : hierarchy.WhereAnyAncestorOrSelf(source, row => row.Node, (row, owner) => owner.Candidate);

        var actual = await query.Select(row => row.Node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([3, 3], actual);
        Assert.Equal(1, probe.CommandCount);
    }
}
