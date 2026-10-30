using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks parameter-identity substitution independently of SQL translation.</summary>
/// <param name="output">Records the paired construction allocation observation.</param>
public sealed class CompositionExpressionTests(ITestOutputHelper output)
{
    /// <summary>Direct substitution reduces an anonymous projection to its selected existing argument.</summary>
    [Fact]
    public void SingleReplacementReducesAnonymousProjectionMembers()
    {
        // Arrange
        var (projection, selector) = Projectors((int value) => new { Value = value }, row => row.Value);

        // Act
        var actual = NestedSetQueryExpressions.Replace(selector.Body, selector.Parameters[0], projection.Body);

        // Assert
        Assert.Same(projection.Parameters[0], actual);
    }

    /// <summary>Direct substitution retains the existing member-initializer projection reduction.</summary>
    [Fact]
    public void SingleReplacementReducesMemberInitializerProjectionMembers()
    {
        // Arrange
        Expression<Func<int, ProjectionRow>> projection = value => new ProjectionRow { Value = value };
        Expression<Func<ProjectionRow, int>> selector = row => row.Value;

        // Act
        var actual = NestedSetQueryExpressions.Replace(selector.Body, selector.Parameters[0], projection.Body);

        // Assert
        Assert.Same(projection.Parameters[0], actual);
    }

    /// <summary>Single substitution leaves a separately bound nested parameter with the same name intact.</summary>
    /// <param name="input">The value replacing the outer binding.</param>
    /// <param name="expected">Whether the independently bound inner value completes the expected sum.</param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void SingleReplacementUsesParameterIdentityInsideNestedLambdas(int input, bool expected)
    {
        // Arrange
        var outer = Expression.Parameter(typeof(int), "value");
        var inner = Expression.Parameter(typeof(int), "value");
        var original = Any(2, inner,
            Expression.Equal(Expression.Add(outer, inner), Expression.Constant(3)));

        // Act
        var replaced = NestedSetQueryExpressions.Replace(original, outer, Expression.Constant(input));
        var actual = Expression.Lambda<Func<bool>>(replaced).Compile()();

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Replacing a projection binding avoids the former synthetic-lambda allocation overhead.</summary>
    [Trait("Category", "Allocation")]
    [Fact]
    public void SingleReplacementAllocatesLessThanSyntheticLambdaSubstitution()
    {
        // Arrange
        Expression<Func<int, ProjectionRow>> projection = value => new ProjectionRow { Value = value };
        Expression<Func<ProjectionRow, int>> selector = row => row.Value;
        Func<object> legacy = () => NestedSetQueryExpressions.Inline(
            Expression.Lambda(selector.Body, selector.Parameters[0]), projection.Body);

        Func<object> replacement = () => NestedSetQueryExpressions.Replace(
            selector.Body, selector.Parameters[0], projection.Body);

        for (var index = 0; index < 100; index++)
        {
            GC.KeepAlive(legacy());
            GC.KeepAlive(replacement());
        }

        // Act
        var allocated = QueryConstructionAllocation.MeasurePaired(legacy, replacement);

        // Assert
        output.WriteLine($"Single replacement: legacy={allocated.Baseline} B/op; "
            + $"replacement={allocated.Expanded} B/op.");

        Assert.InRange(allocated.Expanded, 0, allocated.Baseline - 1);
    }

    /// <summary>Nested lambdas may reuse parameter names without capturing outer arguments.</summary>
    /// <param name="input">The replacement value supplied to the outer row parameter.</param>
    /// <param name="expected">Whether the nested predicate should match.</param>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void NestedLambdasAreSubstitutedByIdentityRatherThanName(
        int input,
        bool expected
    )
    {
        // Arrange
        var row = Expression.Parameter(typeof(int), "row");
        var parent = Expression.Parameter(typeof(int), "parent");
        var node = Expression.Parameter(typeof(int), "node");
        var nestedRow = Expression.Parameter(typeof(int), "row");
        var nestedParent = Expression.Parameter(typeof(int), "parent");
        var nestedNode = Expression.Parameter(typeof(int), "node");
        var external = Expression.Parameter(typeof(int), "row");
        var outerSum = Expression.Add(Expression.Add(Expression.Multiply(row, Expression.Constant(100)),
            Expression.Multiply(parent, Expression.Constant(10))), node);

        var innerSum = Expression.Add(Expression.Add(nestedRow, nestedParent), nestedNode);
        var predicate = Expression.Equal(Expression.Add(outerSum, innerSum), Expression.Constant(157));
        var nested = Any(2, nestedRow, Any(3, nestedParent, Any(5, nestedNode, predicate)));
        var original = Expression.Lambda<Func<int, int, int, bool>>(nested, row, parent, node);
        var replaced = NestedSetQueryExpressions.Inline(
            original, external, Expression.Constant(4), Expression.Constant(7));

        var compiled = Expression.Lambda<Func<int, bool>>(replaced, external).Compile();

        // Act
        var actual = compiled(input);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Same(row, original.Parameters[0]);
        Assert.Same(parent, original.Parameters[1]);
        Assert.Same(node, original.Parameters[2]);
    }

    /// <summary>Builds a nested predicate with an independently bound parameter.</summary>
    private static MethodCallExpression Any(
        int value,
        ParameterExpression parameter,
        Expression predicate
    ) => Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(int)],
        Expression.Constant(new[] { value }), Expression.Lambda<Func<int, bool>>(predicate, parameter));

    /// <summary>Retains the inferred anonymous type while exposing both prepared expression trees.</summary>
    private static (LambdaExpression Projection, LambdaExpression Selector) Projectors<TProjection>(
        Expression<Func<int, TProjection>> projection,
        Expression<Func<TProjection, int>> selector
    ) => (projection, selector);

    /// <summary>Provides a simple initialized projection whose member has one existing expression value.</summary>
    private sealed class ProjectionRow
    {
        /// <summary>Gets the value whose initializer is selected during substitution.</summary>
        public required int Value { get; init; }
    }
}
