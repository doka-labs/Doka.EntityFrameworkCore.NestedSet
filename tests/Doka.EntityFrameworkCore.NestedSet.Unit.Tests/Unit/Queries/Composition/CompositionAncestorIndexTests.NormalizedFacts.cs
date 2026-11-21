using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>
    /// Fact-filter shape and scalar values remain deferred while hierarchy-backed sources stay conservative.
    /// </summary>
    /// <param name="shape">The captured source hiding a hierarchy dependency or an opaque provider root.</param>
    [Theory]
    [InlineData("navigation")]
    [InlineData("named-filter")]
    [InlineData("inherited-filter")]
    [InlineData("raw-sql")]
    [InlineData("ignore-filters")]
    public async Task ToQueryString_NormalizedDependentFactSources_RetainFallback(
        string shape
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>(
            "MySql",
            "Server=127.0.0.1;Database=dependent_facts;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options)
                .UseInternalServiceProvider(provider)
                .Options);

        var ordinary = shape switch
        {
            "raw-sql" => context
                .Set<SelectionFact>()
                .FromSqlRaw("SELECT * FROM SelectionFact"),
            "ignore-filters" => context
                .Set<SelectionFact>()
                .IgnoreQueryFilters(),
            _ => context
                .Set<SelectionFact>()
                .AsQueryable(),
        };

        var named = context.Set<HierarchyFilteredFact>();
        var inherited = context.Set<DerivedHierarchyFilteredFact>();
        Expression<Func<CompositionNode, bool>> selected = shape switch
        {
            "navigation" => node => ordinary.Any(fact => fact.Node!.Id == node.Id),
            "named-filter" => node => named.Any(fact => fact.NodeId == node.Id),
            "inherited-filter" => node => inherited.Any(fact => fact.NodeId == node.Id),
            _ => node => ordinary.Any(fact => fact.NodeId == node.Id),
        };

        var query = context
            .NestedSet<CompositionNode>()
            .ForScope(Guid.Empty)
            .InTree(Guid.Empty)
            .SelectAncestorsOrSelf(
                selected,
                (node, owner, distance) => owner.Id);

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WITH RECURSIVE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Normalized scalar changes reuse EF's compiled query rather than freezing a former capture.</summary>
    /// <param name="operation">The ancestor relation or deferred filter whose rule captures the changing value.</param>
    [Theory]
    [InlineData("ancestors")]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public async Task ToQueryString_NormalizedFactScalarChanges_ReuseOneCompilation(
        string operation
    )
    {
        // Arrange
        var probe = new DeferredCompilationProbe();
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>(
            "MariaDb",
            "Server=127.0.0.1;Database=cached_facts;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options, probe);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options)
                .UseInternalServiceProvider(provider)
                .Options);

        var minimum = 711;
        var facts = context.Set<SelectionFact>();
        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(Guid.Empty)
            .InTree(Guid.Empty);

        var pairs = hierarchy
            .SelectAncestorsOrSelf(
                node => facts.Any(fact => fact.NodeId == node.Id && fact.Id > minimum),
                (node, owner, distance) => new
                {
                    Node = node,
                    Owner = owner,
                    Distance = distance,
                });

        var filtered = operation switch
        {
            "ancestors" => pairs,
            "path" => hierarchy.WherePathEdges(
                pairs,
                row => row.Owner,
                row => row.Node,
                (row, parent, child) => child.Id < minimum),
            "any" => hierarchy.WhereAnyAncestorOrSelf(
                pairs,
                row => row.Node,
                (row, owner) => owner.Id < minimum),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(
                pairs,
                row => row.Node,
                (row, owner) => owner.Id > 0,
                (row, owner) => owner.Id < minimum),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var query = filtered.Select(row => row.Owner.Id);

        // Act
        var first = query.ToQueryString();
        var afterFirst = probe.Compilations;
        minimum = 933;
        var second = query.ToQueryString();

        // Assert
        Assert.Contains("711", first, StringComparison.Ordinal);
        Assert.Contains("933", second, StringComparison.Ordinal);
        Assert.DoesNotContain("711", second, StringComparison.Ordinal);
        Assert.Equal(1, afterFirst);
        Assert.Equal(1, probe.Compilations);
        Assert.Equal(0, probe.RetainedContextOrQueryConstants);
    }

    /// <summary>Checks the completed compile expression without retaining that expression or its context.</summary>
    private sealed class DeferredCompilationProbe : IQueryExpressionInterceptor
    {
        /// <summary>Gets the number of real compilations after parameter extraction.</summary>
        internal int Compilations { get; private set; }

        /// <summary>Gets the number of illegal live context/query constants left in late-generated syntax.</summary>
        internal int RetainedContextOrQueryConstants { get; private set; }

        /// <inheritdoc />
        public Expression QueryCompilationStarting(
            Expression queryExpression,
            QueryExpressionEventData eventData
        )
        {
            Compilations++;
            var visitor = new QueryConstantProbe();
            visitor.Visit(queryExpression);
            RetainedContextOrQueryConstants += visitor.Retained;

            return queryExpression;
        }

        /// <summary>Counts owned object constants while treating EF extension nodes as leaves.</summary>
        private sealed class QueryConstantProbe : ExpressionVisitor
        {
            /// <summary>Gets only detected live object constants, never their values.</summary>
            internal int Retained { get; private set; }

            /// <inheritdoc />
            protected override Expression VisitConstant(
                ConstantExpression node
            )
            {
                if (node.Value is DbContext or IQueryable)
                {
                    Retained++;
                }

                return node;
            }

            /// <inheritdoc />
            protected override Expression VisitExtension(
                Expression node
            ) => node;
        }
    }
}
