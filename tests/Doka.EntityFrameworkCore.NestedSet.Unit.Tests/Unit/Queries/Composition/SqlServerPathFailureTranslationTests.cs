using System.Text.RegularExpressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Guards the SQL Server grouped long-path proof and its exact row-dependent fallback.</summary>
/// <param name="output">The sink retaining actual generated SQL for native qualification.</param>
public sealed partial class SqlServerPathFailureTranslationTests(ITestOutputHelper output)
{
    /// <summary>Independent paths use local grouped coordinates; row-dependent rules retain their own count.</summary>
    /// <param name="rowAware">Whether the edge rule depends on the original occurrence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathProofGroupsOnlyLocalColumns(bool rowAware)
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
        var grouping = GroupingClauses(sql);
        output.WriteLine(sql);

        // Assert
        Assert.Equal(!rowAware, grouping.Length > 0);
        Assert.Equal(rowAware, sql.Contains("COUNT_BIG(*)", StringComparison.Ordinal));
        Assert.DoesNotMatch(@"MAX\s*\(\s*CASE", sql);

        foreach (var clause in grouping)
        {
            Assert.DoesNotContain("SELECT", clause, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MAX(", clause, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>The guard distinguishes a real grouped alias from a scalar subquery embedded in grouping.</summary>
    [Fact]
    public void GroupingGuardRejectsScalarWidthSubqueries()
    {
        // Arrange
        const string invalid = "SELECT MAX(p.Right) FROM Points p GROUP BY p.Left % (SELECT MAX(n.Right) FROM Nodes n)";
        const string valid = "SELECT MAX(p.Right) FROM Points p CROSS JOIN Widths w GROUP BY p.Left % w.Width";

        // Act
        var invalidClause = Assert.Single(GroupingClauses(invalid));
        var validClause = Assert.Single(GroupingClauses(valid));

        // Assert
        Assert.Contains("SELECT", invalidClause, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", validClause, StringComparison.Ordinal);
    }

    /// <summary>Reads grouping expressions in these owned fixtures, respecting nested query parentheses.</summary>
    private static string[] GroupingClauses(string sql)
    {
        var result = new List<string>();

        foreach (Match group in Regex.Matches(sql, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase))
        {
            var start = group.Index + group.Length;
            var end = start;
            var depth = 0;

            // WHY: The closing query boundary needs this exact cursor and parenthesis depth.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (; end < sql.Length; end++)
            {
                if (depth == 0 && (sql.AsSpan(end).StartsWith("HAVING", StringComparison.OrdinalIgnoreCase)
                    || sql.AsSpan(end).StartsWith("ORDER BY", StringComparison.OrdinalIgnoreCase)
                    || sql.AsSpan(end).StartsWith("UNION", StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                if (sql[end] == '(')
                {
                    depth++;
                }
                else if (sql[end] == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
            }

            result.Add(sql[start..end]);
        }

        return result.ToArray();
    }
}
