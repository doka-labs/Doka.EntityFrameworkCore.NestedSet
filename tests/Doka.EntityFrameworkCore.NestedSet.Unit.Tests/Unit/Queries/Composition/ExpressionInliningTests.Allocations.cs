using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ExpressionInliningTests
{
    /// <summary>Direct projection member lookup does not require a visitor or a collection enumerator.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void MemberInitializerLookupAvoidsPerTraversalAllocation()
    {
        // Arrange
        Expression<Func<ProjectionMembers>> projection = () => new ProjectionMembers
        {
            First = 11,
            Ignored = 17,
            Last = 29,
        };

        Expression<Func<ProjectionMembers, int>> selector = row => row.Last;
        const int repetitions = 1000;

        for (var index = 0; index < repetitions; index++)
        {
            GC.KeepAlive(NestedSetQueryExpressions.Inline(selector, projection.Body));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < repetitions; index++)
        {
            GC.KeepAlive(NestedSetQueryExpressions.Inline(selector, projection.Body));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        output.WriteLine($"Member lookup: operations={repetitions}; allocated={allocated} B.");

        // WHY: Fixed noise headroom rejects either the former visitor or enumerator allocation on every lookup.
        Assert.InRange(allocated, 0, 4096);
    }

    /// <summary>Fixed bindings remove the argument array without transferring its cost into larger visitors.</summary>
    /// <param name="arity">The fixed binding count measured against the explicit-array general path.</param>
    [Trait("Category", "Allocation")]
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FixedBindingsAllocateLessThanExplicitArrayBindings(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity);
        var expression = Expression.Lambda(parameters[^1], parameters);
        var first = Expression.Constant(1);
        var second = Expression.Constant(2);
        var third = Expression.Constant(3);
        Func<object> baseline = arity switch
        {
            1 => () => NestedSetQueryExpressions.Inline(expression, new Expression[] { first }),
            2 => () => NestedSetQueryExpressions.Inline(expression, new Expression[] { first, second }),
            _ => () => NestedSetQueryExpressions.Inline(expression, new Expression[] { first, second, third }),
        };

        Func<object> current = arity switch
        {
            1 => () => NestedSetQueryExpressions.Inline(expression, first),
            2 => () => NestedSetQueryExpressions.Inline(expression, first, second),
            _ => () => NestedSetQueryExpressions.Inline(expression, first, second, third),
        };

        for (var index = 0; index < 100; index++)
        {
            GC.KeepAlive(baseline());
            GC.KeepAlive(current());
        }

        // Act
        var allocated = QueryConstructionAllocation.MeasurePaired(baseline, current);

        // Assert
        output.WriteLine($"Arity={arity}; explicit-array={allocated.Baseline} B/op; "
            + $"fixed={allocated.Expanded} B/op; saved={allocated.Baseline - allocated.Expanded} B/op.");

        Assert.InRange(allocated.Expanded, 0, allocated.Baseline - 24);
    }
}
