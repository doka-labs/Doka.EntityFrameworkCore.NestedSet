using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards the SQL Server completed long-path proof and its exact row-dependent fallback.</summary>
/// <param name="output">The sink retaining actual generated SQL for native qualification.</param>
public sealed partial class SqlServerPathFailureTranslationTests(ITestOutputHelper output)
{
    /// <summary>Independent paths complete failure points once; row-dependent rules keep their own count.</summary>
    /// <param name="rowAware">Whether the edge rule depends on the original occurrence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathProofCompletesOnlyRowIndependentFailures(bool rowAware)
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

        var query = rowAware
            ? tree.WherePathEdges(rows, row => row.Upper, row => row.Lower,
                (row, parent, child) => child.EdgeOpen && child.Id <= row.Upper.Id + 100)
            : tree.WherePathEdges(rows, row => row.Upper, row => row.Lower,
                (row, parent, child) => child.EdgeOpen);

        // Act
        var sql = query.Select(row => row.Lower.Id).ToQueryString();
        var fenced = FencedRelations(sql);
        output.WriteLine(sql);

        // Assert
        Assert.Equal(rowAware ? 0 : 1, fenced.Length);
        Assert.All(fenced, AssertUncorrelated);
        Assert.Equal(rowAware, sql.Contains("COUNT_BIG(*)", StringComparison.Ordinal));
        Assert.DoesNotContain("GROUP BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SQRT", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Path and nearest-owner proofs keep caller bounds outside the completed failure relation.</summary>
    /// <param name="nearest">Whether the path belongs to a nearest-owner decision.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedFailurePointsKeepOuterFiltersOutsideTheFence(bool nearest)
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
        output.WriteLine(sql);

        // Assert
        AssertUncorrelated(Assert.Single(FencedRelations(sql)));
        Assert.DoesNotContain("SELECT MAX(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("HAVING", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT_BIG(*)", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The guard rejects a fenced relation that reads an alias declared outside it.</summary>
    [Fact]
    public void CorrelationGuardRejectsOuterAliases()
    {
        // Arrange
        const string inner = "SELECT [f].[Start] FROM [Nodes] AS [f] WHERE [f].[Tree] = @tree";
        const string correlated = "SELECT [f].[Start] FROM [Nodes] AS [f] WHERE [f].[Start] > [t].[Start]";

        // Act
        var error = Record.Exception(() => AssertUncorrelated(correlated));

        // Assert
        AssertUncorrelated(inner);
        Assert.NotNull(error);
    }

    /// <summary>Reads each derived table closed by the completion fence, respecting nested parentheses.</summary>
    private static string[] FencedRelations(string sql)
    {
        var result = new List<string>();

        foreach (Match fence in Regex.Matches(sql, @"ORDER BY \[\w+\]\.\[\w+\]\s+OFFSET 0 ROWS"))
        {
            var depth = 0;
            var start = fence.Index;

            // WHY: Walking backward from the fence needs this exact cursor and parenthesis depth.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (; start > 0; start--)
            {
                if (sql[start - 1] == ')')
                {
                    depth++;
                }
                else if (sql[start - 1] == '(')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
            }

            result.Add(sql[start..fence.Index]);
        }

        return result.ToArray();
    }

    /// <summary>Requires every column reference inside a fenced relation to use an alias declared there.</summary>
    private static void AssertUncorrelated(string relation)
    {
        var declared = Regex.Matches(relation, @"\bAS \[(?<alias>\w+)\]")
            .Select(match => match.Groups["alias"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(Regex.Matches(relation, @"\[(?<alias>\w+)\]\.\["),
            reference => Assert.Contains(reference.Groups["alias"].Value, declared));
    }
}
