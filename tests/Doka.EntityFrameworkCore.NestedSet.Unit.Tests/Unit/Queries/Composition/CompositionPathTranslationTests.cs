using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects the physical Doka path boundary before expensive native capacity qualification.</summary>
public sealed class CompositionPathTranslationTests
{
    /// <summary>Invariant failures stay in an independent relation; row-dependent rules keep their own count.</summary>
    /// <param name="engine">The explicitly configured Doka dialect, without server discovery.</param>
    /// <param name="shape">The invariant path, structural nearest owner, or row-dependent edge rule.</param>
    [Theory]
    [InlineData("MySql", "Path")]
    [InlineData("MySql", "Nearest")]
    [InlineData("MySql", "Correlated")]
    [InlineData("MariaDb", "Path")]
    [InlineData("MariaDb", "Nearest")]
    [InlineData("MariaDb", "Correlated")]
    public async Task PathTranslationRetainsItsQualifiedBoundaryWithoutIo(
        string engine,
        string shape
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_translation;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        // WHY: Dialect-specific translation cases own their service graphs instead of adding them to EF's
        // process-wide cache. Apply the actual options extensions so provider and NestedSet services stay complete.
        await using var provider = BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var scope = Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237");
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var rows = hierarchy.SelectSubtrees(node => node.Id <= 2,
            (upper, lower, distance) => new { Upper = upper, Lower = lower });

        var filtered = shape switch
        {
            "Path" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Lower,
                (row, parent, child) => child.EdgeOpen),
            "Nearest" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Lower,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            "Correlated" => hierarchy.WherePathEdges(rows, row => row.Upper, row => row.Lower,
                (row, parent, child) => child.EdgeOpen && child.Id <= row.Upper.Id + 100),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        // WHY: Translation is synchronous and performs no I/O. This guards the EF boundary only;
        // native capacity cases separately prove actual materialization, semantics and access cost.
        var sql = filtered.Select(row => row.Lower.Id).ToQueryString();

        // Assert
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Empty(context.ChangeTracker.Entries());

        if (shape == "Correlated")
        {
            // WHY: The optional MySQL pair strategy can legitimately fence selected anchor keys.
            // A row-aware path must still use its own count rather than shared invalid-child buckets.
            Assert.DoesNotContain("SUM(", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("COUNT(*)", sql, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("MAX(", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SUM(", sql, StringComparison.OrdinalIgnoreCase);

            if (shape == "Nearest")
            {
                // WHY: Invariant strict candidates use grouped coordinate access rather than a repeated
                // scope-prefix sort. Native cases separately qualify its actual block and fringe work.
                Assert.Contains("CandidateLeft", sql, StringComparison.Ordinal);
                Assert.Contains("CandidateRight", sql, StringComparison.Ordinal);
                Assert.Contains("LeftBlock", sql, StringComparison.Ordinal);
                Assert.Contains("RightBlock", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("`Left` DESC", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("`Depth` DESC", sql, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Only simple row-independent nearest candidates receive shared coordinate partitions.</summary>
    /// <param name="engine">The explicitly configured Doka dialect, without server discovery.</param>
    /// <param name="shape">The independent, row-aware, or query-bearing candidate rule.</param>
    [Theory]
    [InlineData("MySql", "Independent")]
    [InlineData("MySql", "RowAware")]
    [InlineData("MySql", "QueryBearing")]
    [InlineData("MariaDb", "Independent")]
    [InlineData("MariaDb", "RowAware")]
    [InlineData("MariaDb", "QueryBearing")]
    public async Task NearestCandidateBoundaryPreservesApplicationCorrelationWithoutIo(
        string engine,
        string shape
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_translation;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        await using var provider = BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var scope = Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237");
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var rows = context.Set<CompositionNode>().Where(node => node.Scope == scope)
            .Select(node => new { Node = node });

        var filtered = shape switch
        {
            "Independent" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate, (row, owner) => owner.Matches),
            "RowAware" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate && owner.Id < row.Node.Id + 100,
                (row, owner) => owner.Matches),
            "QueryBearing" => hierarchy.WhereNearestAncestorOrSelf(rows, row => row.Node,
                (row, owner) => owner.Candidate
                    && context.Set<CompositionNode>().Any(other => other.ParentId == owner.Id),
                (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var sql = filtered.Select(row => row.Node.Id).ToQueryString();

        // Assert
        // WHY: Failure partitions carry ordinary Left/Right aliases. Candidate-specific aliases identify
        // only the independent owner groups without depending on unrelated DISTINCT or subquery counts.
        Assert.Equal(shape == "Independent", sql.Contains("CandidateLeft", StringComparison.Ordinal));
        Assert.Equal(shape == "Independent", sql.Contains("CandidateRight", StringComparison.Ordinal));
        Assert.Equal(shape == "Independent", sql.Contains("LeftBlock", StringComparison.Ordinal));
        Assert.Equal(shape == "Independent", sql.Contains("RightBlock", StringComparison.Ordinal));

        if (shape != "Independent")
        {
            Assert.Contains("`Left` DESC", sql, StringComparison.Ordinal);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Relational pivot selection stays decorrelatable while scalar sets retain the leaf boundary.</summary>
    /// <param name="engine">The explicitly configured Doka dialect, without server discovery.</param>
    /// <param name="queryBearing">Whether an upward relation selects the downward pivots.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task DownwardSelectionPreservesRelationalPivotPushdownWithoutIo(
        string engine,
        bool queryBearing
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_translation;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        await using var provider = BuildServices(options.Options);
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CompositionNode>()
            .ForScope(Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237"));

        var upward = hierarchy.SelectAncestors(node => node.Id >= 100,
            (node, ancestor, distance) => new { Node = node, Ancestor = ancestor });

        var downward = queryBearing
            ? hierarchy.SelectSubtrees(anchor => upward.Any(row => row.Ancestor == anchor),
                (anchor, node, distance) => new { Anchor = anchor, Node = node })
            : hierarchy.SelectSubtrees(anchor => anchor.Id <= 2,
                (anchor, node, distance) => new { Anchor = anchor, Node = node });

        var boundaryCounter = new DownwardUnionCounter();

        // Act
        // WHY: Translation checks the intended boundary quickly; native Mixed profiles prove its actual plan.
        var sql = downward.Where(row => row.Node.Id == 200).Select(row => row.Anchor.Id).ToQueryString();
        boundaryCounter.Visit(downward.Expression);

        // Assert
        // WHY: The inner upward source may gain a provider-owned recursive UNION during SQL translation.
        // Count the actual downward LINQ boundary instead: a relational pivot must retain zero pair UNIONs.
        Assert.Equal(queryBearing ? 0 : 1, boundaryCounter.Occurrences);
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Counts actual LINQ pair unions without conflating provider-generated source transport SQL.</summary>
    private sealed class DownwardUnionCounter : ExpressionVisitor
    {
        /// <summary>Gets the number of Queryable.Union boundaries in the constructed downward expression.</summary>
        internal int Occurrences { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable) && node.Method.Name == nameof(Queryable.Union))
            {
                Occurrences++;
            }

            return base.VisitMethodCall(node);
        }
    }

    /// <summary>Owns a complete isolated EF service graph for each explicit dialect translation.</summary>
    private static ServiceProvider BuildServices(
        DbContextOptions options
    )
    {
        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        return services.BuildServiceProvider();
    }
}
