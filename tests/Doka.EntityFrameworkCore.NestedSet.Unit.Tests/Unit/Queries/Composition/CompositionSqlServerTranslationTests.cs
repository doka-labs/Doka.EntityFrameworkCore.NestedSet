namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks complete SQL Server query translation without claiming runtime coverage on an ARM host.</summary>
public sealed class CompositionSqlServerTranslationTests
{
    /// <summary>Complete path, nearest and mixed queries translate without opening a connection.</summary>
    /// <param name="shape">The complete query shape being translated.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("nearest")]
    [InlineData("mixed")]
    public void CompleteQueryTranslatesWithoutIo(
        string shape
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>()
            .UseSqlServer(
                "Server=localhost;Database=composition_translation;Integrated Security=true",
                provider => provider.UseNestedSets())
            .AddInterceptors(boundary, boundary.Commands)
            .Options;

        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: Metadata-only translation probes own their provider graph. A new interceptor must not
        // add a process-global singleton graph or depend on which other test collections ran first.
        using var provider = services.BuildServiceProvider();
        using var context = new CompositionGuardContext(
            new DbContextOptionsBuilder<CompositionGuardContext>(options).UseInternalServiceProvider(provider).Options);

        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var down = hierarchy.SelectSubtrees(node => node.Id == 1,
            (upper, lower, distance) => new CompositionGuardRow { Upper = upper, Lower = lower, Distance = distance });

        var query = shape == "mixed" ? Mixed(hierarchy, down) : CompositionEndpointTests.Filter(hierarchy, down, shape);
        var scalar = query.Select(row => new { AnchorId = row.Upper.Id, NodeId = row.Lower.Id, row.Distance });

        // Act
        var sql = scalar.ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(shape == "mixed", sql.Contains("COUNT_BIG(", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);

        // WHY: The mixed leg now retains grouped ancestor identities and a correlated zero-or-one
        // owner lookup. Its caller-dependent edge rule must still use the exact COUNT_BIG fallback.
        if (shape == "mixed")
        {
            Assert.Contains("CROSS APPLY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SELECT TOP(1)", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.DoesNotContain("APPLY", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Retains both the original node and pivot while joining an upward leg to a downward leg.</summary>
    private static IQueryable<CompositionGuardRow> Mixed(
        NestedSet<CompositionGuardNode> hierarchy,
        IQueryable<CompositionGuardRow> down
    )
    {
        var up = hierarchy.SelectAncestors(node => node.Id == 3,
            (lower, upper, distance) => new CompositionGuardRow { Upper = upper, Lower = lower, Distance = distance });

        var mixed = up.Join(down, ancestor => ancestor.Upper.Id, descendant => descendant.Upper.Id,
            (ancestor, descendant) => new CompositionGuardRow
            {
                Upper = ancestor.Upper,
                Lower = descendant.Lower,
                Payload = ancestor.Lower,
                Distance = descendant.Distance,
            });

        return hierarchy.WherePathEdges(mixed, row => row.Upper, row => row.Lower,
            (row, parent, child) => row.Payload != null && child.Id != row.Payload.Id);
    }
}
