using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionEndpointProvenanceTests
{
    /// <summary>Retains an ordinary mapped alias in an explicitly selected application row.</summary>
    private static IQueryable<CompositionGuardRow> Project(
        IQueryable<CompositionGuardNode> nodes
    )
        => nodes.Select(node => new CompositionGuardRow
        {
            Upper = node,
            Lower = node,
        });

    /// <summary>Reads the source of the public presentation ordering without comparing predicate values.</summary>
    private static Expression BoundSource(
        IQueryable<CompositionGuardNode> nodes
    )
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
            "path" => tree.WherePathEdges(
                source,
                node => node,
                node => node,
                (row, parent, child) => true),
            "any" => tree.WhereAnyAncestorOrSelf(
                source,
                node => node,
                (row, owner) => true),
            "nearest" => tree.WhereNearestAncestorOrSelf(
                source,
                node => node,
                (row, owner) => true,
                (row, owner) => true),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    /// <summary>Seeds matching keys in a foreign scope and a foreign tree alongside one hidden local node.</summary>
    /// <param name="context">The context used by this fixture operation.</param>
    /// <param name="scope">The primary scope populated by the fixture.</param>
    /// <param name="foreignScope">A separate scope used to reject foreign provenance.</param>
    /// <param name="treeId">The primary tree identity.</param>
    /// <param name="foreignTreeId">A separate tree identity used to reject foreign provenance.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task SeedAsync(
        CompositionContext context,
        Guid scope,
        Guid foreignScope,
        Guid treeId,
        Guid foreignTreeId,
        CancellationToken cancellationToken
    )
    {
        // WHY: Use the node ordinal to seed matching identities and bounds in both compared trees.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var id = 1; id <= 3; id++)
        {
            var node = new CompositionNode
            {
                Scope = scope,
                Id = id,
                TreeId = treeId,
                ParentId = id == 1
                    ? null
                    : id - 1,
                Left = id,
                Right = 7 - id,
                Depth = id - 1,
                Visible = id != 2,
            };

            await context.AddAsync(node, cancellationToken);
        }

        await context.AddRangeAsync(
            [
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
            ],
            cancellationToken);

        await context.SavePrecomputedHierarchyAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    /// <summary>Counts expression-root occurrences before SQL optimization can fold redundant subqueries.</summary>
    private sealed class EntityRootCounter : ExpressionVisitor
    {
        private int _count;

        /// <summary>Counts actual root occurrences, including deliberately repeated references.</summary>
        internal static int Count(
            Expression expression
        )
        {
            var counter = new EntityRootCounter();
            counter.Visit(expression);

            return counter._count;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        )
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
