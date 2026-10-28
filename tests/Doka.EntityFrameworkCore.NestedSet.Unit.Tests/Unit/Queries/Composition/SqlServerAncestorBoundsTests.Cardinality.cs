using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerAncestorBoundsTests
{
    /// <summary>Excluded leaf intervals collapse per tree before an origin can repeat their coordinate relation.</summary>
    /// <param name="trees">The independent trees sharing one scope.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task IndependentCoordinatesRetainOnlyEligibleOwnersAndOneExcludedClassPerTree(int trees)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CoordinateBoundsContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets());

        await using var services = NestedSetTestAdapters.BuildServices(options.Options);
        await using var context = new CoordinateBoundsContext(options.UseInternalServiceProvider(services).Options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var nodes = new List<CoordinateBoundsNode>();

        // WHY: These exact fixture ordinals determine independent tree identities and their node-key ranges.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var tree = 0; tree < trees; tree++)
        {
            var rootId = tree * 1_000;
            var treeId = tree == 0 ? "a" : "b";
            nodes.Add(BoundsNode(rootId, 1, 202, tree: treeId));

            // WHY: Each ordinal determines the adjacent integer interval and its exact parent key.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var leaf = 1; leaf <= 100; leaf++)
            {
                var child = BoundsNode(rootId + leaf, 2L * leaf, (2L * leaf) + 1,
                    parent: rootId, depth: 1, tree: treeId);

                child.Position = leaf - 1L;
                nodes.Add(child);
            }
        }

        await context.AddRangeAsync(nodes, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var root = new NestedSetQuery<CoordinateBoundsNode>(context,
            context.Model.FindEntityType(typeof(CoordinateBoundsNode))!).ForScope(7);

        var pairs = CoordinatePairs(root, node => node.Depth == 1);
        var probe = new IndependentCoordinateRelationProbe();
        probe.Visit(pairs.Expression);
        var relation = Assert.IsAssignableFrom<MethodCallExpression>(probe.Relation);
        var count = Expression.Call(typeof(Queryable), nameof(Queryable.Count), relation.Method.GetGenericArguments(), relation);

        // Act
        // WHY: Count the actual independent production relation before containment and hydration. Correct
        // final ancestors alone would hide one unnecessary retained coordinate class per excluded leaf.
        var classes = await ((IAsyncQueryProvider)pairs.Provider).ExecuteAsync<Task<int>>(count, CancellationToken.None);

        // Assert
        Assert.Equal(2 * trees, classes);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Finds the owned coordinate boundary without constructing an alternative implementation.</summary>
    private sealed class IndependentCoordinateRelationProbe : ExpressionVisitor
    {
        /// <summary>Gets the production DISTINCT relation counted by the regression.</summary>
        internal MethodCallExpression? Relation { get; private set; }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Method.Name == nameof(Queryable.Distinct)
                && node.Method.GetGenericArguments()[0].Name == "AncestorBounds")
            {
                Relation = node;
            }

            return base.VisitMethodCall(node);
        }
    }
}
