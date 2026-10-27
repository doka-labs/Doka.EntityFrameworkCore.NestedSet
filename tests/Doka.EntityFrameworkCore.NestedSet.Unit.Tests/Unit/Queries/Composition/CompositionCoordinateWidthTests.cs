using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects lazy coordinate-expression reuse without freezing a width or a later scope binding.</summary>
public sealed class CompositionCoordinateWidthTests
{
    /// <summary>A rebound query has its own deferred root while repeated use retains the original expression.</summary>
    [Fact]
    public void RebindingDoesNotReuseAnEarlierCoordinateRoot()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var entity = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var first = new NestedSetQuery<CompositionGuardNode>(context, entity).ForScope(1_873_125);
        var width = Width(first);
        var rebound = first.ForScope(1_873_126);
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);

        // Act
        var repeated = Width(first);
        var next = Width(rebound);
        var originalSql = Project(nodes, width).ToQueryString();
        var reboundSql = Project(nodes, next).ToQueryString();

        // Assert
        Assert.Same(width, repeated);
        Assert.NotSame(width, next);
        Assert.Contains("1873125", originalSql, StringComparison.Ordinal);
        Assert.DoesNotContain("1873126", originalSql, StringComparison.Ordinal);
        Assert.Contains("1873125", reboundSql, StringComparison.Ordinal);
        Assert.Contains("1873126", reboundSql, StringComparison.Ordinal);
        Assert.Contains("MAX", reboundSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Invokes the actual private builder without exposing a diagnostic-only public API.</summary>
    private static Expression Width(NestedSetQuery<CompositionGuardNode> query)
        => typeof(NestedSetQuery<CompositionGuardNode>)
            .GetMethod("VisibleCoordinateBlockWidth", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<Func<Expression>>(query)();

    /// <summary>Translates the retained width expression without evaluating its aggregate or opening SQLite.</summary>
    private static IQueryable<long> Project(IQueryable<CompositionGuardNode> nodes, Expression width)
    {
        var node = Expression.Parameter(typeof(CompositionGuardNode), "node");
        var select = Expression.Call(typeof(Queryable), nameof(Queryable.Select),
            [typeof(CompositionGuardNode), typeof(long)], nodes.Expression,
            Expression.Quote(Expression.Lambda<Func<CompositionGuardNode, long>>(width, node)));

        return nodes.Provider.CreateQuery<long>(select);
    }
}
