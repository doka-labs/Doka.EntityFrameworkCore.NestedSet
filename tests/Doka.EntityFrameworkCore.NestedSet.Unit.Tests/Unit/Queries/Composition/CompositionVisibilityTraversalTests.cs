using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks exact-bound reuse and conservative traversal of external query expressions.</summary>
/// <param name="output">Captures allocation evidence outside the measured operation.</param>
// WHY: Concurrent collections can perturb thread allocation counters. Use the existing isolated
// measurement collection without relaxing the budget or serializing unrelated regression tests.
[Collection("Allocation measurements")]
public sealed partial class CompositionVisibilityTraversalTests(ITestOutputHelper output)
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
    public void SharedQuerySubtreeRetainsVisibilityAcrossOperations(int references, int operations)
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
        for (var index = 0; index < operations; index++)
        {
            visible &= NestedSetQueryEndpoint.Require(source, node => node, entityType, bound, "node").IsVisible;
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
    public void SharedBinaryQueryGraphRetainsVisibleEndpoint(int depth)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var shared = nodes.Expression;

        for (var index = 0; index < depth; index++)
        {
            shared = Expression.Call(typeof(Queryable), nameof(Queryable.Concat),
                [typeof(CompositionGuardNode)], shared, shared);
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
    public void DistinctQuerySubtreesAreAllProved(int queries, bool unsafeLastQuery)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var sources = Enumerable.Range(0, queries)
            .Select(index => Where(unsafeLastQuery && index == queries - 1
                ? nodes.IgnoreQueryFilters().Expression
                : nodes.Expression, Expression.Constant(true))).ToArray();

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
    public void SharedQueryContainingOpaqueExtensionRetainsEveryVisit(bool rejectSecondVisit)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var forbidden = Any(nodes.IgnoreQueryFilters().Expression);
        var extension = new ChangingPredicate(rejectSecondVisit ? forbidden : Expression.Constant(true));
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
    public void SharedParentRetainsCustomAcceptDispatches(bool rejectSecondVisit)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var forbidden = Any(nodes.IgnoreQueryFilters().Expression);
        var extension = new ChangingDispatch(rejectSecondVisit ? forbidden : Expression.Constant(true));
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
    public void SharedParentRetainsCustomMethodMetadataInspections(bool rejectSecondVisit)
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
    public void SharedParentRetainsCustomMemberMetadataInspections(bool rejectSecondVisit)
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
    public void LaterOperationStillRejectsUnsafeSharedQuery()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var safe = Where(nodes.Expression, Expression.Constant(true));
        var unsafeQuery = Where(nodes.IgnoreQueryFilters().Expression, Expression.Constant(true));
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
    public void SharedQueryNeverEvaluatesCapturedQueryGetter()
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

    /// <summary>Increasing references to existing query nodes does not allocate a cache entry per occurrence.</summary>
    [Fact]
    public void SharedReferencesDoNotIncreaseProofAllocation()
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

        for (var index = 0; index < 64; index++)
        {
            NestedSetQueryEndpoint.Require(single, selector, entityType, bound, "node");
            NestedSetQueryEndpoint.Require(repeated, selector, entityType, bound, "node");
        }

        // Act
        var singleBytes = Allocated(single, selector, entityType, bound);
        var repeatedBytes = Allocated(repeated, selector, entityType, bound);

        // Assert
        output.WriteLine($"Operations=128; single={singleBytes} B; repeated={repeatedBytes} B.");
        Assert.InRange(repeatedBytes - singleBytes, -256L * 128, 256L * 128);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Creates one visible mapped source without executing or translating it.</summary>
    private static IQueryable<CompositionGuardNode> Nodes(CompositionGuardContext context)
        => context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty).Nodes;

    /// <summary>Embeds actual queries in a predicate so endpoint resolution does not visit the probes.</summary>
    private static IQueryable<CompositionGuardNode> Source(
        IQueryable<CompositionGuardNode> nodes,
        IEnumerable<Expression> sources
    )
    {
        var tests = sources.Select(Any).ToArray();
        var predicate = tests.Aggregate(Expression.AndAlso);
        var node = Expression.Parameter(typeof(CompositionGuardNode), "node");

        return nodes.Where(Expression.Lambda<Func<CompositionGuardNode, bool>>(predicate, node));
    }

    /// <summary>Uses the public Queryable Any metadata without evaluating a query or compiling a delegate.</summary>
    private static Expression Any(Expression source)
        => Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(CompositionGuardNode)], source);

    /// <summary>Creates an ordinary immutable query operator with runtime-owned reflection metadata.</summary>
    private static MethodCallExpression Where(Expression source, Expression predicate)
    {
        var node = Expression.Parameter(typeof(CompositionGuardNode), "node");

        return Expression.Call(s_where, source,
            Expression.Quote(Expression.Lambda<Func<CompositionGuardNode, bool>>(predicate, node)));
    }

    /// <summary>Measures synchronous construction after both shapes and metadata paths are warm.</summary>
    private static long Allocated(
        IQueryable<CompositionGuardNode> source,
        Expression<Func<CompositionGuardNode, CompositionGuardNode>> selector,
        IEntityType entityType,
        Expression bound
    )
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 128; index++)
        {
            NestedSetQueryEndpoint.Require(source, selector, entityType, bound, "node");
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
