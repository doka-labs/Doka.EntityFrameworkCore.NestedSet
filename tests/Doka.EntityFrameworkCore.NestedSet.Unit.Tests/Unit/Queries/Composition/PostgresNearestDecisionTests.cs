using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects PostgreSQL's single projected nearest decision and existing depth ordering.</summary>
public sealed class PostgresNearestDecisionTests
{
    /// <summary>Each candidate form selects once by depth without starting an owner-key rejoin.</summary>
    /// <param name="shape">The independent, row-aware, or query-bearing candidate contract.</param>
    [Theory]
    [InlineData("independent")]
    [InlineData("row-aware")]
    [InlineData("query-bearing")]
    public async Task NearestDecisionSelectsDepthOnceWithoutIo(string shape)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>("PostgreSql"))
            .AddInterceptors(boundary);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);
        var rows = hierarchy.Nodes.Select(node => new { Node = node });
        var nearest = shape switch
        {
            "independent" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            "row-aware" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate && owner.Id < row.Node.Id + 100,
                (row, owner) => owner.Matches),
            "query-bearing" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate
                    && context.Set<CompositionNode>().Any(other => other.ParentId == owner.Id),
                (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var sql = nearest.Select(row => row.Node.Id).ToQueryString();
        var selections = Regex.Matches(sql, "ORDER BY [^\\r\\n]*\"Depth\" DESC");

        // Assert
        Assert.Single(selections);
        Assert.DoesNotContain("\"Left\" DESC", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A one-anchor broad result cannot hide repeated materialization of the full payload population.</summary>
    [Fact]
    public void RepeatedBroadPayloadExceedsTheUnchangedPairWorkBudget()
    {
        // Arrange
        const int nodes = 100_000;
        const string plan = """
            [{"Plan":{"Node Type":"Aggregate","Actual Rows":1,"Actual Loops":1,
                "Plans":[{"Node Type":"Materialize","Actual Rows":50000,"Actual Loops":100000}]}}]
            """;

        var budget = CompositionPlanWorkBudget.ForPairs(nodes, anchors: 1, facts: nodes, contributions: nodes);

        // Act
        var work = CompositionPlanWork.ReadPostgreSqlJson(plan);

        // Assert
        Assert.Equal(1, work.RootOutputTuples);
        Assert.Equal(5_000_000_000D, work.MaximumOperatorTuples);
        Assert.Equal(400_002, budget.OperatorTuples);
        Assert.True(work.MaximumOperatorTuples > budget.OperatorTuples);
    }
}
