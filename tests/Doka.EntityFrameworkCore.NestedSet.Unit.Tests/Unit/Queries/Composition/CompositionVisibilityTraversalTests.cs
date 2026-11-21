using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks exact-bound reuse and conservative traversal of external query expressions.</summary>

public sealed partial class CompositionVisibilityTraversalTests
{
    private static readonly MethodInfo s_where = ((MethodCallExpression)
        ((Expression<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>)
            (query => query.Where(node => true))).Body).Method;

    /// <summary>Repeated references to a proven subtree retain visibility across independent operations.</summary>
    /// <param name="references">The number of references to the same query subtree.</param>
    /// <param name="operations">The number of independent endpoint operations.</param>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(32, 1)]
    [InlineData(256, 2)]
    public void Require_SharedQuerySubtree_RetainsVisibilityAcrossOperations(
        int references,
        int operations
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var shared = Where(nodes.Expression, Expression.Constant(true));
        var source = Source(nodes, Enumerable.Repeat(shared, references));
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        var visible = true;

        // Act
        // WHY: Accumulate visibility across repeated operations to verify each operation establishes its own proof.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < operations; index++)
        {
            visible &= NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node")
                .IsVisible;
        }

        // Assert
        Assert.True(visible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A shared binary query graph stays deferred and preserves the bound endpoint.</summary>
    /// <param name="depth">The number of shared binary query layers rather than expanded expression copies.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(16)]
    public void Require_SharedBinaryQueryGraph_RetainsVisibleEndpoint(
        int depth
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var shared = nodes.Expression;

        // WHY: Each layer must reuse the prior expression twice so the fixture remains a shared graph.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < depth; index++)
        {
            shared = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Concat),
                [typeof(CompositionGuardNode)],
                shared,
                shared);
        }

        var source = Source(nodes, [shared]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node");

        // Assert
        Assert.True(actual.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Equal-looking but independently constructed query nodes are never merged by value or shape.</summary>
    /// <param name="queries">The number of independently constructed query nodes.</param>
    /// <param name="unsafeLastQuery">Whether only the final query disables global filters.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(32, false)]
    [InlineData(128, false)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(32, true)]
    [InlineData(128, true)]
    public void Require_DistinctQuerySubtrees_AreAllProved(
        int queries,
        bool unsafeLastQuery
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var sources = Enumerable
            .Range(0, queries)
            .Select(index => Where(
                unsafeLastQuery && index == queries - 1
                    ? nodes.IgnoreQueryFilters()
                        .Expression
                    : nodes.Expression,
                Expression.Constant(true)))
            .ToArray();

        var source = Source(nodes, sources);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node");

        // Assert
        Assert.Equal(!unsafeLastQuery, actual.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>An opaque extension is revisited even when a parent query expression is shared.</summary>
    /// <param name="rejectSecondVisit">Whether the extension exposes an unsafe modifier on its second visit.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Require_SharedQueryContainingOpaqueExtension_RetainsEveryVisit(
        bool rejectSecondVisit
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var forbidden = Any(
            nodes.IgnoreQueryFilters()
                .Expression);

        var extension = new ChangingPredicate(
            rejectSecondVisit
                ? forbidden
                : Expression.Constant(true));

        var shared = Where(nodes.Expression, extension);
        var source = Source(nodes, [shared, shared]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node");

        // Assert
        Assert.Equal(!rejectSecondVisit, actual.IsVisible);
        Assert.Equal(2, extension.Visits);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Custom Accept dispatch cannot hide a changed query modifier inside a cached parent.</summary>
    /// <param name="rejectSecondVisit">Whether the second dispatch exposes an unsafe query modifier.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Require_SharedParent_RetainsCustomAcceptDispatches(
        bool rejectSecondVisit
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var forbidden = Any(
            nodes.IgnoreQueryFilters()
                .Expression);

        var extension = new ChangingDispatch(
            rejectSecondVisit
                ? forbidden
                : Expression.Constant(true));

        var shared = Where(nodes.Expression, extension);
        var source = Source(nodes, [shared, shared]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node");

        // Assert
        Assert.Equal(!rejectSecondVisit, actual.IsVisible);
        Assert.Equal(2, extension.Visits);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A custom method's metadata is inspected on every visit, including when its parent is shared.</summary>
    /// <param name="rejectSecondVisit">Whether the second inspection reports an unsafe query modifier.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Require_SharedParent_RetainsCustomMethodMetadataInspections(
        bool rejectSecondVisit
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var probe = new QueryMethodProbe(rejectSecondVisit);
        var shared = Where(probe.Where(nodes.Expression, Expression.Constant(true)), Expression.Constant(true));
        var source = Source(nodes, [shared, shared]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        probe.Reset();

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node");

        // Assert
        Assert.Equal(!rejectSecondVisit, actual.IsVisible);
        Assert.Equal(2, probe.DeclaringTypeReads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A custom member's reported query type is inspected again without executing its CLR getter.</summary>
    /// <param name="rejectSecondVisit">Whether the second inspection reports a captured query type.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Require_SharedParent_RetainsCustomMemberMetadataInspections(
        bool rejectSecondVisit
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var holder = new MetadataHolder();
        var probe = new QueryMemberProbe(rejectSecondVisit);
        var member = Expression.Property(Expression.Constant(holder), probe);
        var shared = Where(nodes.Expression, member);
        var source = Source(nodes, [shared, shared]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        probe.Reset();

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node");

        // Assert
        Assert.Equal(!rejectSecondVisit, actual.IsVisible);
        Assert.InRange(probe.TypeReads, 2, int.MaxValue);
        Assert.Equal(0, holder.GetterReads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A later independent unsafe source cannot inherit an earlier positive proof.</summary>
    [Fact]
    public void Require_LaterOperationStill_RejectsUnsafeSharedQuery()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var safe = Where(nodes.Expression, Expression.Constant(true));
        var unsafeQuery = Where(
            nodes.IgnoreQueryFilters()
                .Expression,
            Expression.Constant(true));

        var safeSource = Source(nodes, [safe, safe]);
        var unsafeSource = Source(nodes, [safe, safe, unsafeQuery]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var before = NestedSetQueryEndpoint.Require(safeSource, node => node, entityType, bound, "node");
        var after = NestedSetQueryEndpoint.Require(unsafeSource, node => node, entityType, bound, "node");

        // Assert
        Assert.True(before.IsVisible);
        Assert.False(after.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Repeated captured query getters remain untrusted and are never evaluated by a proof.</summary>
    [Fact]
    public void Require_SharedQuery_NeverEvaluatesCapturedQueryGetter()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var trap = new QueryGetterTrap();
        var captured = Expression.Property(Expression.Constant(trap), nameof(QueryGetterTrap.Query));
        var shared = Where(nodes.Expression, Any(captured));
        var source = Source(nodes, [shared, shared]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node");

        // Assert
        Assert.False(actual.IsVisible);
        Assert.Equal(0, trap.Reads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Repeated references retain the same mapping and visibility as one existing subtree.</summary>
    [Fact]
    public void Require_SharedReferences_RetainTheIndependentProofResult()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var shared = Where(nodes.Expression, Expression.Constant(true));
        var single = Source(nodes, [shared]);
        var repeated = Source(nodes, Enumerable.Repeat(shared, 256));
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        Expression<Func<CompositionGuardNode, CompositionGuardNode>> selector = node => node;

        // Act
        var one = NestedSetQueryEndpoint.Require(single, selector, entityType, bound, "node");
        var many = NestedSetQueryEndpoint.Require(repeated, selector, entityType, bound, "node");

        // Assert
        Assert.Same(one.EntityType, many.EntityType);
        Assert.True(one.IsVisible);
        Assert.Equal(one.IsVisible, many.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }
}
