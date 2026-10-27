using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Doka.EntityFrameworkCore.NestedSet.Providers;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionAncestorIndexTests
{
    /// <summary>Only explicit independent fact roots can cross the ancestor source-completion boundary.</summary>
    /// <param name="shape">The fact source, hidden hierarchy dependency, or opaque query carrier.</param>
    /// <param name="requiresCorrelation">Whether the existing decorrelatable strategy must remain available.</param>
    [Theory]
    [InlineData("plain", false)]
    [InlineData("facts", false)]
    [InlineData("inherited-facts", false)]
    [InlineData("tracking-marker", false)]
    [InlineData("scalar-property", false)]
    [InlineData("hierarchy", true)]
    [InlineData("navigation", true)]
    [InlineData("navigation-property", true)]
    [InlineData("named-filter", true)]
    [InlineData("inherited-filter", true)]
    [InlineData("opaque-query", true)]
    [InlineData("query-getter", true)]
    [InlineData("query-object", true)]
    [InlineData("unknown-operator", true)]
    [InlineData("raw-sql", true)]
    [InlineData("custom-root", true)]
    [InlineData("enumerable-custom-root", true)]
    public async Task FactSelectionKeepsHierarchyAndOpaqueSourcesDecorrelatable(
        string shape,
        bool requiresCorrelation
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<IndexCompositionContext>("MySql",
            "Server=127.0.0.1;Database=fact_selection;User ID=unused;Pooling=false");

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options).UseInternalServiceProvider(provider).Options);

        var holder = new QueryHolder(context.Set<SelectionFact>());
        var opaqueRoot = new OpaqueFactRoot(context.Model.FindEntityType(typeof(SelectionFact))!);
        var selection = shape switch
        {
            "plain" => (Expression<Func<CompositionNode, bool>>)(node => node.Id > 0),
            "facts" => FactSelection(context.Set<SelectionFact>(), (node, fact) => fact.NodeId == node.Id),
            "inherited-facts" => FactSelection(context.Set<DerivedSelectionFact>(),
                (node, fact) => fact.NodeId == node.Id),
            "tracking-marker" => FactSelection(context.Set<SelectionFact>().AsNoTracking(),
                (node, fact) => fact.NodeId == node.Id),
            "scalar-property" => FactSelection(context.Set<SelectionFact>(),
                (node, fact) => EF.Property<int>(fact, nameof(SelectionFact.NodeId)) == node.Id),
            "hierarchy" => FactSelection(context.Set<CompositionNode>(), (node, other) => other.Id == node.Id),
            "navigation" => FactSelection(context.Set<SelectionFact>(),
                (node, fact) => fact.Node!.Id == node.Id),
            "navigation-property" => FactSelection(context.Set<SelectionFact>(),
                (node, fact) => EF.Property<CompositionNode>(fact, nameof(SelectionFact.Node))!.Id == node.Id),
            "named-filter" => FactSelection(context.Set<HierarchyFilteredFact>(),
                (node, fact) => fact.NodeId == node.Id),
            "inherited-filter" => FactSelection(context.Set<DerivedHierarchyFilteredFact>(),
                (node, fact) => fact.NodeId == node.Id),
            "opaque-query" => node => holder.Field.Any(fact => fact.NodeId == node.Id),
            "query-getter" => node => holder.ThrowingQuery.Any(fact => fact.NodeId == node.Id),
            "query-object" => node => ((IQueryable<SelectionFact>)holder.Untyped).Any(fact => fact.NodeId == node.Id),
            "unknown-operator" => FactSelection(context.Set<SelectionFact>().IgnoreQueryFilters(),
                (node, fact) => fact.NodeId == node.Id),
            "raw-sql" => FactSelection(context.Set<SelectionFact>().FromSqlRaw("SELECT * FROM SelectionFacts"),
                (node, fact) => fact.NodeId == node.Id),
            "custom-root" => FactSelection(context.Set<SelectionFact>().AsQueryable().Provider
                    .CreateQuery<SelectionFact>(opaqueRoot), (node, fact) => fact.NodeId == node.Id),
            "enumerable-custom-root" => OpaqueEnumerableSelection(opaqueRoot),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var actual = NestedSetQuerySelection.RequiresAncestorDecorrelation(selection, context.Model,
            out var factSelection);

        // Assert
        Assert.Equal(requiresCorrelation, actual);
        Assert.Equal(!requiresCorrelation && shape != "plain", factSelection);
        Assert.Equal(0, holder.GetterCalls);
        Assert.Equal(0, opaqueRoot.Visits);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A fact EXISTS gets the indexed strategy only with the actual required provider capabilities.</summary>
    /// <param name="engine">The explicitly configured native engine.</param>
    /// <param name="includeSelf">Whether the ordered inclusive edge is required.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task FactSelectedAncestorTranslationRemainsDeferred(
        string engine,
        bool includeSelf
    )
    {
        // Arrange
        var io = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<IndexCompositionContext>(
                ModelCompatibilityDatabase.Options<IndexCompositionContext>(engine,
                    "Server=127.0.0.1;Database=fact_selection;User ID=unused;Pooling=false"))
            .AddInterceptors(io).Options;

        await using var provider = SelectionServices(options);
        await using var context = new IndexCompositionContext(
            new DbContextOptionsBuilder<IndexCompositionContext>(options).UseInternalServiceProvider(provider).Options);

        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty).InTree(Guid.Empty);
        var selected = FactSelection(context.Set<SelectionFact>(),
            (node, fact) => fact.Scope == node.Scope && fact.NodeId == node.Id);

        var enabled = NestedSetProviderCapabilities.Resolve(context).CompletedSourceMethod is not null
            && (!includeSelf || NestedSetProviderCapabilities.Resolve(context).OrderedJoinMethod is not null);

        // Act
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(selected, (node, owner, distance) => new { node.Id, Distance = distance })
            : hierarchy.SelectAncestors(selected, (node, owner, distance) => new { node.Id, Distance = distance });

        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(enabled, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(enabled && includeSelf, sql.Contains("STRAIGHT_JOIN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);

        if (enabled)
        {
            var sourceSql = sql[..sql.IndexOf("UNION ALL", StringComparison.OrdinalIgnoreCase)];
            Assert.Contains("SELECT DISTINCT", sourceSql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0, io.ConnectionStarts);
        Assert.Equal(0, io.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Embeds an owned EF root without capturing a query getter or evaluating the predicate.</summary>
    private static Expression<Func<CompositionNode, bool>> FactSelection<TFact>(
        IQueryable<TFact> facts,
        Expression<Func<CompositionNode, TFact, bool>> match
    )
    {
        var exists = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(TFact)], facts.Expression,
            Expression.Quote(Expression.Lambda<Func<TFact, bool>>(match.Body, match.Parameters[1])));

        return Expression.Lambda<Func<CompositionNode, bool>>(exists, match.Parameters[0]);
    }

    /// <summary>Places an opaque child inside the pre-pass's separate row-reference traversal.</summary>
    private static Expression<Func<CompositionNode, bool>> OpaqueEnumerableSelection(Expression root)
    {
        Expression<Func<CompositionNode, SelectionFact, bool>> match = (node, fact) => fact.NodeId == node.Id;
        var exists = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(SelectionFact)], root,
            Expression.Lambda<Func<SelectionFact, bool>>(match.Body, match.Parameters[1]));

        return Expression.Lambda<Func<CompositionNode, bool>>(exists, match.Parameters[0]);
    }

    /// <summary>Owns each test's provider services instead of growing EF's process-wide singleton cache.</summary>
    private static ServiceProvider SelectionServices(DbContextOptions options, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: EF rejects singleton interceptors in AddInterceptors when an explicit provider owns
        // services. Register compilation observers in that same owned graph, after injected interceptors.
        foreach (var interceptor in interceptors)
        {
            services.AddSingleton(interceptor);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Provides an ordinary scalar-filtered fact root with an optional hierarchy navigation.</summary>
    private class SelectionFact
    {
        /// <summary>Gets or sets the independent fact identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the tenant shared with the referenced node.</summary>
        public Guid Scope { get; set; }

        /// <summary>Gets or sets the measured node key.</summary>
        public int NodeId { get; set; }

        /// <summary>Gets or sets the scalar application filter.</summary>
        public bool Visible { get; set; }

        /// <summary>Gets or sets a navigation that must retain hierarchy-dependent routing.</summary>
        public CompositionNode? Node { get; set; }
    }

    /// <summary>Hides a hierarchy dependency in the second named EF filter.</summary>
    private class HierarchyFilteredFact
    {
        /// <summary>Gets or sets the independent fact identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the node selected by the hidden relational filter.</summary>
        public int NodeId { get; set; }

        /// <summary>Gets or sets the harmless first named filter value.</summary>
        public bool Visible { get; set; }
    }

    /// <summary>Retains an inherited ordinary fact filter without introducing a hierarchy dependency.</summary>
    private sealed class DerivedSelectionFact : SelectionFact;

    /// <summary>Must inspect the hierarchy-bearing named filter declared only on its mapped base.</summary>
    private sealed class DerivedHierarchyFilteredFact : HierarchyFilteredFact;

    /// <summary>Proves conservative inspection never calls application query getters.</summary>
    private sealed class QueryHolder(IQueryable<SelectionFact> query)
    {
        /// <summary>Retains an opaque captured query without exposing its expression as owned syntax.</summary>
        public readonly IQueryable<SelectionFact> Field = query;

        /// <summary>Hides a captured query behind a non-query declared type.</summary>
        public readonly object Untyped = query;

        /// <summary>Gets the number of application getter invocations.</summary>
        public int GetterCalls { get; private set; }

        /// <summary>Gets a query only by executing application code that analysis must not call.</summary>
        public IQueryable<SelectionFact> ThrowingQuery
        {
            get
            {
                GetterCalls++;

                throw new InvalidOperationException("A query getter must remain unevaluated.");
            }
        }
    }

    /// <summary>A derived EF root must not obtain trust or execute its application visitor callback.</summary>
    /// <param name="entityType">The otherwise valid fact metadata hidden behind the opaque root.</param>
    private sealed class OpaqueFactRoot(IEntityType entityType) : EntityQueryRootExpression(entityType)
    {
        /// <summary>Gets the number of application-controlled child dispatches.</summary>
        public int Visits { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            Visits++;

            throw new InvalidOperationException("A custom query root must remain uninspected.");
        }
    }
}
