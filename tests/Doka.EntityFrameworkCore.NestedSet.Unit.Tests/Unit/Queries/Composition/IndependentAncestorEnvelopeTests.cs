using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Providers;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks bounded unindexed ancestor construction without evaluating application selection or doing I/O.</summary>
public sealed class IndependentAncestorEnvelopeTests
{
    /// <summary>The optional source binder supplies necessary limits while preserving the legacy provider path.</summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    /// <param name="treeBound">Whether the caller binds one tree instead of querying the scope's forest.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task StructuralSourceBoundsTranslateWithoutAnAncestorIndexOrIo(string engine, bool treeBound)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=unindexed_envelope;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        await using var provider = services.BuildServiceProvider();
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty);
        var query = treeBound
            ? hierarchy.InTree(Guid.Empty).SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        var enabled = NestedSetProviderCapabilities.Resolve(context).CompletedSourceMethod is not null
            && NestedSetProviderCapabilities.Resolve(context).OrderedJoinMethod is not null;

        var expectedCompletions = enabled
            ? engine == "MySql" ? 2 : 1
            : 0;

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(enabled, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(enabled, sql.Contains("MAX(", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(enabled, sql.Contains("MIN(", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("HasChildren", sql, StringComparison.Ordinal);
        // WHY: Each completed source has one recursive dead arm. MySQL needs a second boundary
        // against semijoin pullout; applying that extra materialization to MariaDB regressed branched chains.
        Assert.Equal(expectedCompletions, sql.Split("UNION ALL", StringSplitOptions.None).Length - 1);

        if (enabled)
        {
            Assert.Contains("`Scope` >=", sql, StringComparison.Ordinal);
            Assert.Contains("`Scope` <=", sql, StringComparison.Ordinal);
            Assert.Contains("`TreeId` >=", sql, StringComparison.Ordinal);
            Assert.Contains("`TreeId` <=", sql, StringComparison.Ordinal);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Only owner payload hydration is directed after matching, while source hydration remains ordinary.</summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task MatchedOwnerHydrationHasItsOwnDirectedSqlEdge(string engine)
    {
        // Arrange
        await using var context = new CompositionContext(ModelCompatibilityDatabase.Options<CompositionContext>(engine));
        var query = context.NestedSet<CompositionNode>().ForScope(Guid.Empty)
            .SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        var enabled = NestedSetProviderCapabilities.Resolve(context).CompletedSourceMethod is not null
            && NestedSetProviderCapabilities.Resolve(context).OrderedJoinMethod is not null;

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(enabled ? 1 : 0, sql.Split("STRAIGHT_JOIN", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("SELECT STRAIGHT_JOIN", sql, StringComparison.OrdinalIgnoreCase);

        if (enabled)
        {
            // WHY: An available method and one directive do not prove the right edge is constrained.
            // Follow the actual projected aliases so accidentally directing source hydration fails too.
            var joins = AncestorIndexHydrationJoins.Read(sql, "CompositionNodes");
            Assert.Equal("INNER JOIN", joins.Source);
            Assert.Equal("STRAIGHT_JOIN", joins.Owner);
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Only one caller selection remains, including when a relational pivot keeps the unfenced route.</summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    /// <param name="queryBearing">Whether the predicate refers to a hierarchy query that can become a pivot.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    public async Task ApplicationSelectionIsRetainedOnceWithoutEvaluation(string engine, bool queryBearing)
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<CompositionContext>(engine);
        await using var context = new CompositionContext(options);
        Expression<Func<CompositionNode, bool>> selected = queryBearing
            ? node => context.Set<CompositionNode>().Any(other => other.Id == node.Id && SelectNode(other.Id))
            : node => SelectNode(node.Id);

        var hierarchy = context.NestedSet<CompositionNode>().ForScope(Guid.Empty);

        // Act
        var query = hierarchy.SelectAncestors(selected,
            (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        var counter = new SelectionCounter();
        counter.Visit(query.Expression);

        // Assert
        Assert.Equal(1, counter.Occurrences);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A volatile server predicate is emitted once in the completed source, never copied to its envelope.</summary>
    /// <param name="engine">The explicitly configured Doka engine.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public async Task VolatileServerSelectionOccursOnceInTheStatement(string engine)
    {
        // Arrange
        await using var context = new CompositionContext(ModelCompatibilityDatabase.Options<CompositionContext>(engine));
        var query = context.NestedSet<CompositionNode>().ForScope(Guid.Empty)
            .SelectAncestors(node => EF.Functions.Random() >= 0,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(1, sql.Split("RAND()", StringSplitOptions.None).Length - 1);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Independent facts keep their query block while hidden hierarchy dependencies retain decorrelation.</summary>
    /// <param name="engine">The explicitly configured native Doka dialect.</param>
    /// <param name="shape">An owned root, ordinary capture, or a hierarchy-bearing fact source.</param>
    [Theory]
    [InlineData("MySql", "embedded")]
    [InlineData("MySql", "captured")]
    [InlineData("MySql", "hierarchy")]
    [InlineData("MariaDb", "embedded")]
    [InlineData("MariaDb", "captured")]
    [InlineData("MariaDb", "hierarchy")]
    public async Task IndependentFactsRetainTheirCompletedSourceBoundary(string engine, string shape)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionContext>(
                ModelCompatibilityDatabase.Options<CompositionContext>(engine,
                    "Server=127.0.0.1;Database=unindexed_facts;User ID=unused;Pooling=false"))
            .AddInterceptors(boundary);

        var services = new ServiceCollection();

        foreach (var extension in options.Options.Extensions)
        {
            extension.ApplyServices(services);
        }

        await using var provider = services.BuildServiceProvider();
        await using var context = new CompositionContext(options.UseInternalServiceProvider(provider).Options);
        var facts = context.Set<CompositionInputOne>().Where(fact => fact.Amount > 0);
        var selection = shape switch
        {
            "embedded" => FactSelection(facts, (node, fact) => fact.Scope == node.Scope && fact.NodeId == node.Id),
            "captured" => node => facts.Any(fact => fact.Scope == node.Scope && fact.NodeId == node.Id),
            "hierarchy" => node => facts.Any(fact => fact.Scope == node.Scope && fact.NodeId == node.Id
                && context.Set<CompositionNode>().Any(hidden => hidden.Id == fact.NodeId)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var query = context.NestedSet<CompositionNode>().ForScope(Guid.Empty)
            .SelectAncestors(selection,
                (source, owner, distance) => new { Source = source.Id, Owner = owner.Id, Distance = distance });

        var enabled = shape != "hierarchy" && NestedSetProviderCapabilities.Resolve(context).CompletedSourceMethod is not null
            && NestedSetProviderCapabilities.Resolve(context).OrderedJoinMethod is not null;

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(enabled, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(enabled, sql.Contains("MAX(", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(enabled, sql.Contains("MIN(", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);

        if (enabled)
        {
            var completedSource = sql[..sql.IndexOf("UNION ALL", StringComparison.OrdinalIgnoreCase)];
            Assert.Contains("SELECT DISTINCT", completedSource, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Embeds an actual independent EF root without evaluating or capturing its query object.</summary>
    private static Expression<Func<CompositionNode, bool>> FactSelection(
        IQueryable<CompositionInputOne> facts,
        Expression<Func<CompositionNode, CompositionInputOne, bool>> match
    )
    {
        var exists = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(CompositionInputOne)],
            facts.Expression,
            Expression.Quote(Expression.Lambda<Func<CompositionInputOne, bool>>(match.Body, match.Parameters[1])));

        return Expression.Lambda<Func<CompositionNode, bool>>(exists, match.Parameters[0]);
    }

    /// <summary>Marks deferred application code that construction must never execute.</summary>
    private static bool SelectNode(int key)
        => throw new InvalidOperationException($"Selection for {key} must remain deferred.");

    /// <summary>Counts the exact caller marker rather than unrelated provider operations.</summary>
    private sealed class SelectionCounter : ExpressionVisitor
    {
        /// <summary>Gets the number of retained caller selection occurrences.</summary>
        internal int Occurrences { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(IndependentAncestorEnvelopeTests)
                && node.Method.Name == nameof(SelectNode))
            {
                Occurrences++;
            }

            return base.VisitMethodCall(node);
        }
    }
}
