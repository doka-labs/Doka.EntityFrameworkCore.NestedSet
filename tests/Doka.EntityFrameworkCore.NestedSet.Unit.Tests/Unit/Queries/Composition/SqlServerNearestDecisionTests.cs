using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects SQL Server's depth-ordered projected nearest selection at the default compatibility level.</summary>
public sealed class SqlServerNearestDecisionTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Retains the default-level SQL Server translation for native qualification.</summary>
    /// <param name="output">The test-owned diagnostic sink.</param>
    public SqlServerNearestDecisionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>A single owner selection projects acceptance and complete-path evidence without a key rejoin.</summary>
    /// <param name="rowAware">Whether the candidate rule depends on the original selected occurrence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NearestDecisionSelectsDepthOnceWithoutIo(bool rowAware)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>("SqlServer"))
            .AddInterceptors(boundary);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);
        var rows = hierarchy.Nodes.Select(node => new { Node = node });
        var nearest = rowAware
            ? hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate && owner.Id < row.Node.Id + 100,
                (row, owner) => owner.Matches)
            : hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches);

        // Act
        var sql = nearest.Select(row => row.Node.Id).ToQueryString();
        _output.WriteLine(sql);
        var selections = Regex.Matches(sql, "ORDER BY [^\\r\\n]*\\[Depth\\] DESC");

        // Assert
        Assert.Single(selections);
        Assert.Contains("SELECT TOP(1) CASE", sql, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(sql, @"COUNT_BIG\(\*\)"));
        Assert.DoesNotContain("SUM(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[Left] DESC", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
