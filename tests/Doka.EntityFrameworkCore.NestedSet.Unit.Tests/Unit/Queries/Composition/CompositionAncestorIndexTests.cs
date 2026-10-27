using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies deferred indexed ancestor construction with both published and feature-enabled Doka binaries.</summary>
public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>The optional path uses its configured flag and completed structural selection without I/O.</summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    /// <param name="includeSelf">Whether equal-bound owners participate.</param>
    /// <param name="treeBound">Whether both aliases already share one explicit tree identity.</param>
    [Theory]
    [InlineData("MySql", false, false)]
    [InlineData("MySql", false, true)]
    [InlineData("MySql", true, false)]
    [InlineData("MySql", true, true)]
    [InlineData("MariaDb", false, false)]
    [InlineData("MariaDb", false, true)]
    [InlineData("MariaDb", true, false)]
    [InlineData("MariaDb", true, true)]
    public async Task IndexedAncestorTranslationKeepsSourceEnvelopeDeferred(
        string engine,
        bool includeSelf,
        bool treeBound
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<IndexCompositionContext>(
                ModelCompatibilityDatabase.Options<IndexCompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_index;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        await using var provider = services.BuildServiceProvider();
        await using var context = new IndexCompositionContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CompositionNode>()
            .ForScope(Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237"));

        var query = treeBound
            ? includeSelf
                ? hierarchy.InTree(Guid.Empty).SelectAncestorsOrSelf(node => node.Id == 3,
                    (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance })
                : hierarchy.InTree(Guid.Empty).SelectAncestors(node => node.Id == 3,
                    (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance })
            : includeSelf
                ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                    (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance })
                : hierarchy.SelectAncestors(node => node.Id == 3,
                    (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance });

        // WHY: This same contract runs against the published fallback and exact private feature
        // binary. Availability changes the SQL strategy; neither installation may perform I/O here.
        var hasBinding = NestedSetProviderCapabilities.Resolve(context).CompletedSourceMethod is not null
            && (!includeSelf || NestedSetProviderCapabilities.Resolve(context).OrderedJoinMethod is not null);

        // Act
        var sql = query.ToQueryString();
        var structural = new StrictCoordinateContainmentCounter();
        structural.Visit(query.Expression);

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(hasBinding, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(hasBinding && includeSelf,
            sql.Contains("STRAIGHT_JOIN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("HasChildren", sql, StringComparison.Ordinal);

        if (!includeSelf)
        {
            Assert.Equal(hasBinding ? 1 : 0, structural.CompletePredicates);
        }

        if (hasBinding)
        {
            Assert.Contains("`HasChildren` >=", sql, StringComparison.Ordinal);
            Assert.Contains("`HasChildren` <=", sql, StringComparison.Ordinal);

            // WHY: Scalar source predicates do not need the fact-selection query-block boundary.
            // Guard its absence so that a fact-only plan correction cannot add work to every query.
            var sourceSql = sql[..sql.IndexOf("UNION ALL", StringComparison.OrdinalIgnoreCase)];
            Assert.DoesNotContain("SELECT DISTINCT", sourceSql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Source completion retains the caller predicate once, including a correlated selection fallback.</summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    /// <param name="includeSelf">Whether equal-bound owners participate.</param>
    /// <param name="queryBearing">Whether the predicate contains a relational query operator.</param>
    [Theory]
    [InlineData("MySql", false, false)]
    [InlineData("MySql", false, true)]
    [InlineData("MySql", true, false)]
    [InlineData("MySql", true, true)]
    [InlineData("MariaDb", false, false)]
    [InlineData("MariaDb", false, true)]
    [InlineData("MariaDb", true, false)]
    [InlineData("MariaDb", true, true)]
    public async Task IndexedAncestorSelectionOccursExactlyOnce(
        string engine,
        bool includeSelf,
        bool queryBearing
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<IndexCompositionContext>(
                ModelCompatibilityDatabase.Options<IndexCompositionContext>(engine,
                    "Server=127.0.0.1;Database=composition_index;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        await using var provider = services.BuildServiceProvider();
        await using var context = new IndexCompositionContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CompositionNode>()
            .ForScope(Guid.Parse("820bc0eb-7a3c-4dd9-9cbb-87d758889237"));

        Expression<Func<CompositionNode, bool>> selected = queryBearing
            ? node => context.Set<CompositionNode>().Any(other => other.Id == node.Id && SelectedNode(other.Id))
            : node => SelectedNode(node.Id);

        // Act
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(selected,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance })
            : hierarchy.SelectAncestors(selected,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance });

        var counter = new SelectionCounter();
        counter.Visit(query.Expression);

        // Assert
        Assert.Equal(1, counter.Occurrences);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Marks application code that must never execute during deferred construction.</summary>
    private static bool SelectedNode(int key)
        => throw new InvalidOperationException($"Selection for node {key} must remain deferred.");

    /// <summary>Counts the caller's exact marker rather than matching unrelated query operators.</summary>
    private sealed class SelectionCounter : ExpressionVisitor
    {
        /// <summary>Gets the number of marker calls in the final query expression.</summary>
        internal int Occurrences { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(CompositionAncestorIndexTests)
                && node.Method.Name == nameof(SelectedNode))
            {
                Occurrences++;
            }

            return base.VisitMethodCall(node);
        }
    }
}
