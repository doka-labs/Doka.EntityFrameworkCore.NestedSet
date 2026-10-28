using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards completed local path summaries and the indexed maximum-coordinate lookup.</summary>
public sealed partial class SqlServerPathSummaryBoundaryTests
{
    /// <summary>Outer path filters follow local grouping instead of correlating its HAVING or width scan.</summary>
    /// <param name="nearest">Whether the path belongs to a nearest-owner decision.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScalarFailureSummariesKeepOuterFiltersAfterCompletedGrouping(bool nearest)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>("SqlServer"))
            .AddInterceptors(boundary);

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(services).Options);
        var tree = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);
        var rows = tree.SelectSubtrees(node => node.Id == 1,
            (upper, lower, distance) => new { Upper = upper, Lower = lower });

        var query = nearest
            ? tree.WhereNearestAncestorOrSelf(rows, row => row.Lower,
                (row, owner) => owner.Candidate && owner.Id != row.Lower.Id, (row, owner) => owner.Matches)
            : tree.WherePathEdges(rows, row => row.Upper, row => row.Lower, (_, parent, child) => child.EdgeOpen);

        // Act
        var sql = query.Select(row => row.Lower.Id).ToQueryString();
        TestContext.Current.TestOutputHelper?.WriteLine(sql);

        // Assert
        Assert.Equal(2, Regex.Count(sql, @"SELECT MAX\("));
        Assert.Equal(2, Regex.Count(sql, "OFFSET 0 ROWS"));
        Assert.Equal(2, Regex.Count(sql, @"ORDER BY [^\r\n]*\[Right\] DESC"));
        Assert.DoesNotMatch(@"MAX\s*\(\s*CASE", sql);
        Assert.DoesNotContain("HAVING", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT_BIG(*)", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
