using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks conservative provenance without weakening external endpoint membership or query filters.</summary>
public sealed class CompositionEndpointProvenanceTests
{
    /// <summary>Only guaranteed aliases of the exact bound expression carry visibility provenance.</summary>
    /// <param name="shape">The source operator or competing root carrying the selected alias.</param>
    /// <param name="expected">Whether the source proves membership in the exact bound root.</param>
    [Theory]
    [InlineData("nodes", true)]
    [InlineData("pairs", true)]
    [InlineData("join", true)]
    [InlineData("concat", true)]
    [InlineData("union", true)]
    [InlineData("safe-modifiers", true)]
    [InlineData("raw", false)]
    [InlineData("same-values", false)]
    [InlineData("another-facade", false)]
    [InlineData("mixed-concat", false)]
    [InlineData("mixed-union", false)]
    [InlineData("mixed-intersect", false)]
    [InlineData("mixed-except", false)]
    [InlineData("captured-inner", false)]
    [InlineData("ignore-filters", false)]
    [InlineData("payload-ignore-filters", false)]
    [InlineData("unknown-modifier", false)]
    [InlineData("default-if-empty", false)]
    [InlineData("optional-left", false)]
    [InlineData("optional-right", false)]
    public void AliasProvenanceRequiresTheExactGuaranteedBoundRoot(
        string shape,
        bool expected
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var treeId = Guid.NewGuid();
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(treeId);

        var nodes = tree.Nodes;
        var raw = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);
        var payloads = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var other = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(treeId).Nodes;

        var projected = Project(nodes);
        var source = shape switch
        {
            "nodes" => projected,
            "pairs" => tree.SelectSubtrees(node => node.Id == 1,
                (upper, lower, distance) => new CompositionGuardRow { Upper = upper, Lower = lower }),
            "join" => nodes.Join(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            "concat" => projected.Concat(projected),
            "union" => projected.Union(projected),
            "safe-modifiers" => Project(nodes.AsTracking().TagWith("provenance")),
            "raw" => Project(raw),
            "same-values" => Project(raw.Where(node => node.Scope == 1 && node.TreeId == treeId)),
            "another-facade" => Project(other),
            "mixed-concat" => projected.Concat(Project(other)),
            "mixed-union" => projected.Union(Project(other)),
            "mixed-intersect" => projected.Intersect(Project(other)),
            "mixed-except" => projected.Except(Project(other)),
            "captured-inner" => payloads.SelectMany(payload => nodes,
                (payload, node) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            "ignore-filters" => Project(nodes.IgnoreQueryFilters()),
            "payload-ignore-filters" => nodes.Join(payloads.IgnoreQueryFilters(), node => node.Id,
                payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            "unknown-modifier" => Project(nodes.IgnoreAutoIncludes()),
            "default-if-empty" => Project(nodes.DefaultIfEmpty().Select(node => node!)),
            "optional-left" => payloads.LeftJoin(nodes, payload => payload.Id, node => node.Id,
                (payload, node) => new CompositionGuardRow { Upper = node!, Lower = node!, Payload = payload }),
            "optional-right" => nodes.RightJoin(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node!, Lower = node!, Payload = payload }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, row => row.Lower,
            entityType, BoundSource(nodes), "node");

        // Assert
        Assert.Same(entityType, actual.EntityType);
        Assert.Equal(expected, actual.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Proven aliases remove only the membership roots redundant with their source.</summary>
    /// <param name="operation">The public filter applied to the same entity mapping.</param>
    /// <param name="removedRoots">The endpoint-membership roots absent only from the proven query.</param>
    [Theory]
    [InlineData("path", 2)]
    [InlineData("any", 1)]
    [InlineData("nearest", 1)]
    public void ExactBoundAliasesRemoveOnlyRedundantMembershipRoots(
        string operation,
        int removedRoots
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var treeId = Guid.NewGuid();
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(treeId);

        var uncertain = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .Where(node => node.Scope == 1 && node.TreeId == treeId);

        // Act
        var provenQuery = Filter(tree, tree.Nodes, operation);
        var uncertainQuery = Filter(tree, uncertain, operation);

        // Assert
        Assert.Equal(removedRoots,
            EntityRootCounter.Count(uncertainQuery.Expression) - EntityRootCounter.Count(provenQuery.Expression));

        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>External and modified sources retain scope/tree boundaries through real asynchronous SQL.</summary>
    /// <param name="operation">The public filter whose endpoint membership is exercised.</param>
    /// <param name="shape">The bound, external, foreign, mixed, or explicitly unfiltered endpoint source.</param>
    [Theory]
    [InlineData("path", "bound")]
    [InlineData("any", "bound")]
    [InlineData("nearest", "bound")]
    [InlineData("path", "raw")]
    [InlineData("any", "raw")]
    [InlineData("nearest", "raw")]
    [InlineData("path", "same-values")]
    [InlineData("any", "same-values")]
    [InlineData("nearest", "same-values")]
    [InlineData("path", "another-facade")]
    [InlineData("any", "another-facade")]
    [InlineData("nearest", "another-facade")]
    [InlineData("path", "foreign-scope")]
    [InlineData("any", "foreign-scope")]
    [InlineData("nearest", "foreign-scope")]
    [InlineData("path", "foreign-tree")]
    [InlineData("any", "foreign-tree")]
    [InlineData("nearest", "foreign-tree")]
    [InlineData("path", "mixed-concat")]
    [InlineData("any", "mixed-concat")]
    [InlineData("nearest", "mixed-concat")]
    [InlineData("path", "ignore-filters")]
    [InlineData("any", "ignore-filters")]
    [InlineData("nearest", "ignore-filters")]
    [InlineData("path", "payload-ignore-filters")]
    [InlineData("any", "payload-ignore-filters")]
    [InlineData("nearest", "payload-ignore-filters")]
    [InlineData("path", "default-if-empty")]
    [InlineData("any", "default-if-empty")]
    [InlineData("nearest", "default-if-empty")]
    public async Task EndpointMembershipStillRejectsForeignIdentities(
        string operation,
        string shape
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = new DbContextOptionsBuilder<CompositionContext>()
            .UseSqlite(connection, provider => provider.UseNestedSets())
            .Options;

        await using var context = new CompositionContext(options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var scope = Guid.NewGuid();
        var foreignScope = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var foreignTreeId = Guid.NewGuid();
        await SeedAsync(context, scope, foreignScope, treeId, foreignTreeId);
        var tree = context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId);
        var raw = context.Set<CompositionNode>();
        var otherScope = context.NestedSet<CompositionNode>().ForScope(foreignScope).InTree(treeId).Nodes;
        var source = shape switch
        {
            "bound" => tree.Nodes,
            "raw" => raw,
            "same-values" => raw.Where(node => node.Scope == scope && node.TreeId == treeId),
            "another-facade" => context.NestedSet<CompositionNode>().ForScope(scope).InTree(treeId).Nodes,
            "foreign-scope" => otherScope,
            "foreign-tree" => context.NestedSet<CompositionNode>().ForScope(scope).InTree(foreignTreeId).Nodes,
            "mixed-concat" => tree.Nodes.Concat(otherScope),
            "ignore-filters" => raw.IgnoreQueryFilters(),
            "payload-ignore-filters" => tree.Nodes.Join(raw.IgnoreQueryFilters(),
                node => new { node.Scope, node.Id }, payload => new { payload.Scope, payload.Id },
                (node, payload) => node),
            "default-if-empty" => tree.Nodes.Where(node => node.Id < 0).DefaultIfEmpty().Select(node => node!),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        int[] expected = shape switch
        {
            "foreign-scope" or "foreign-tree" or "default-if-empty" => [],
            "ignore-filters" or "payload-ignore-filters" => [1, 2, 3],
            _ => [1, 3],
        };

        // Act
        var actual = await Filter(tree, source, operation).OrderBy(node => node.Id)
            .Select(node => node.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Retains an ordinary mapped alias in an explicitly selected application row.</summary>
    private static IQueryable<CompositionGuardRow> Project(IQueryable<CompositionGuardNode> nodes)
        => nodes.Select(node => new CompositionGuardRow { Upper = node, Lower = node });

    /// <summary>Reads the source of the public presentation ordering without comparing predicate values.</summary>
    private static Expression BoundSource(IQueryable<CompositionGuardNode> nodes)
    {
        // WHY: Nodes adds presentation OrderBy over the private immutable binding. Its actual argument is
        // reused by composition; rebuilding an equivalent predicate here would prove the wrong contract.
        return ((MethodCallExpression)nodes.Expression).Arguments[0];
    }

    /// <summary>Exercises each filter with self endpoints so membership is independent of path complexity.</summary>
    private static IQueryable<TNode> Filter<TNode>(
        NestedSetTree<TNode, Guid> tree,
        IQueryable<TNode> source,
        string operation
    )
        where TNode : class
        => operation switch
        {
            "path" => tree.WherePathEdges(source, node => node, node => node, (row, parent, child) => true),
            "any" => tree.WhereAnyAncestorOrSelf(source, node => node, (row, owner) => true),
            "nearest" => tree.WhereNearestAncestorOrSelf(source, node => node,
                (row, owner) => true, (row, owner) => true),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    /// <summary>Seeds matching keys in a foreign scope and a foreign tree alongside one hidden local node.</summary>
    private static async Task SeedAsync(
        CompositionContext context,
        Guid scope,
        Guid foreignScope,
        Guid treeId,
        Guid foreignTreeId
    )
    {
        for (var id = 1; id <= 3; id++)
        {
            var node = new CompositionNode
            {
                Scope = scope,
                Id = id,
                TreeId = treeId,
                ParentId = id == 1 ? null : id - 1,
                Left = id,
                Right = 7 - id,
                Depth = id - 1,
                Visible = id != 2,
            };

            await context.AddAsync(node, CancellationToken.None);
        }

        await context.AddRangeAsync([
            new CompositionNode
            {
                Scope = foreignScope,
                Id = 1,
                TreeId = treeId,
                Left = 1,
                Right = 2,
            },
            new CompositionNode
            {
                Scope = scope,
                Id = 4,
                TreeId = foreignTreeId,
                Left = 1,
                Right = 2,
            },
        ], CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }

    /// <summary>Counts expression-root occurrences before SQL optimization can fold redundant subqueries.</summary>
    private sealed class EntityRootCounter : ExpressionVisitor
    {
        private int _count;

        /// <summary>Counts actual root occurrences, including deliberately repeated references.</summary>
        internal static int Count(Expression expression)
        {
            var counter = new EntityRootCounter();
            counter.Visit(expression);

            return counter._count;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression)
            {
                _count++;

                return node;
            }

            return base.VisitExtension(node);
        }
    }
}
