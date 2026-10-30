namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Independent origins select their own nearest eligible pivot without a global minimum.</summary>
    [Fact]
    public async Task NearestPivotIsChosenIndependentlyForEachOrigin()
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var pairs = hierarchy.SelectAncestors(node => node.Id == 3 || node.Id == 6,
            (source, pivot, distance) => new { Source = source, Pivot = pivot, Distance = distance });

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                pairs,
                row => row.Source,
                (row, owner) => owner.Candidate,
                (row, owner) => owner == row.Pivot)
            .Select(row => new { Source = row.Source.Id, Pivot = row.Pivot.Id, row.Distance })
            .OrderBy(row => row.Source)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([(3, 2, 1), (6, 1, 2)], actual.Select(row => (row.Source, row.Pivot, row.Distance)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>Ranks stay local to each origin, including origins with too few eligible ancestors.</summary>
    /// <param name="rank">The one-based application rank after filtering eligible ancestors.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NthEligiblePivotRankIsCorrelatedPerOrigin(
        int rank
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        IReadOnlyDictionary<int, int?> parents = new Dictionary<int, int?>
        {
            [1] = null, [2] = 1, [3] = 2, [4] = 2, [5] = 1, [6] = 5,
        };

        int[] origins = [3, 6];
        var expected = origins.SelectMany(source => Ancestors(parents, source, includeSelf: false)
                .Where(owner => owner is 1 or 2)
                .Skip(rank - 1)
                .Take(1),
            (source, owner) => (Source: source, Pivot: owner, Distance: Distance(parents, owner, source)))
            .ToArray();

        var pairs = hierarchy.SelectAncestors(node => node.Id == 3 || node.Id == 6,
            (source, pivot, distance) => new { Source = source, Pivot = pivot, Distance = distance });

        var eligible = pairs.Where(row => row.Pivot.Candidate);

        // Act
        var actual = await eligible.Where(row => eligible.Count(other => other.Source == row.Source
                && other.Distance < row.Distance) == rank - 1)
            .Select(row => new { Source = row.Source.Id, Pivot = row.Pivot.Id, row.Distance })
            .OrderBy(row => row.Source)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(row => (row.Source, row.Pivot, row.Distance)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }

    /// <summary>Joining unique anchors back to payload rows restores their own criteria and provenance.</summary>
    /// <param name="secondAccepted">Whether the second occurrence accepts its own selected candidate.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UniqueAnchorSelectionRestoresEachPayloadBeforeOwnerFiltering(
        bool secondAccepted
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, probe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var node = context.Set<CompositionNode>().Where(entity => entity.Scope == scope && entity.Id == 3);
        var payloads = node.Select(entity => new { Node = entity, CandidateId = 1, AcceptedId = 1, Payload = 11 })
            .Concat(node.Select(entity => new
            {
                Node = entity, CandidateId = 2, AcceptedId = secondAccepted ? 2 : 1, Payload = 22,
            }));

        var pairs = hierarchy.SelectAncestors(source => payloads.Any(row => row.Node == source),
            (source, pivot, distance) => new { Source = source, Pivot = pivot });

        // Act
        var restored = pairs.Join(payloads, pair => pair.Source, input => input.Node, (pair, input) => new
        {
            pair.Source, pair.Pivot, input.CandidateId, input.AcceptedId, input.Payload,
        });

        var actual = await hierarchy.WhereNearestAncestorOrSelf(
                restored,
                row => row.Source,
                (row, owner) => owner.Id == row.CandidateId,
                (row, owner) => owner == row.Pivot && owner.Id == row.AcceptedId)
            .Select(row => new { row.Payload, Pivot = row.Pivot.Id })
            .OrderBy(row => row.Payload)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        (int Payload, int Pivot)[] expected = secondAccepted ? [(11, 1), (22, 2)] : [(11, 1)];

        Assert.Equal(expected, actual.Select(row => (row.Payload, row.Pivot)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }
}
