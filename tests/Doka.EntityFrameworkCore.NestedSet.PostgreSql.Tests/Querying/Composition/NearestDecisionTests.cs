namespace Doka.EntityFrameworkCore.NestedSet.Tests.PostgreSql;

/// <summary>Executes projected PostgreSQL nearest decisions without changing their selection contracts.</summary>
[Collection("Model compatibility")]
public sealed class NearestDecisionTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the PostgreSQL collection-owned model database.</summary>
    /// <param name="fixture">The provider fixture owning the independent scope seeded by each test.</param>
    public NearestDecisionTests(ProviderFixture<ModelCompatibilityDatabase, PostgreSqlEngine> fixture) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Selection precedes acceptance and complete-path checks, preserving original occurrences.</summary>
    /// <param name="mode">The accepted, rejected, hidden-path, self, or row-correlated candidate variant.</param>
    /// <param name="expectedCount">The independently known number of accepted occurrences.</param>
    [Theory]
    [InlineData("near-reject", 0)]
    [InlineData("near-accept", 2)]
    [InlineData("far-accept", 2)]
    [InlineData("self-reject", 0)]
    [InlineData("hidden-path", 0)]
    [InlineData("row-aware", 1)]
    public async Task ProjectedDecisionPreservesSelectionPathAndOriginalBag(string mode, int expectedCount)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CompositionTestData.CreateContextAsync(
            _fixture, Engine, scope, probe, hidden: mode == "hidden-path" ? 2 : null);

        var treeId = await context.Set<CompositionNode>().Where(node => node.Scope == scope && node.Id == 1)
            .Select(node => node.TreeId).SingleAsync(CancellationToken.None);

        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId);
        var candidateId = mode switch
        {
            "far-accept" or "hidden-path" or "row-aware" => 1,
            "self-reject" => 3,
            _ => 2,
        };

        var acceptedId = mode == "near-accept" ? 2 : 1;
        var first = hierarchy.Nodes.Where(node => node.Id == 3)
            .Select(node => new { Node = node, CandidateId = candidateId, AcceptedId = acceptedId, Occurrence = 1 });

        var second = hierarchy.Nodes.Where(node => node.Id == 3)
            .Select(node => new
            {
                Node = node,
                CandidateId = mode == "row-aware" ? 2 : candidateId,
                AcceptedId = acceptedId,
                Occurrence = 2,
            });

        var rows = first.Concat(second);
        probe.Reset();

        // Act
        var actual = await hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Id == row.CandidateId || owner.Id == 1,
                (row, owner) => owner.Id == row.AcceptedId)
            .OrderBy(row => row.Occurrence).Select(row => row.Node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedCount, actual.Length);
        Assert.All(actual, node => Assert.Equal(3, node));
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
