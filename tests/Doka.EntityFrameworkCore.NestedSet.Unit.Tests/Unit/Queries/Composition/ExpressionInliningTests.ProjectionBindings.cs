using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ExpressionInliningTests
{
    /// <summary>Direct member lookup returns the selected assignment without traversing unrelated values.</summary>
    [Fact]
    public void Inline_MemberInitializerLookup_ReusesSelectedAssignment()
    {
        // Arrange
        var probe = new GetterProbe();
        Expression<Func<ProjectionMembers>> projection = () => new ProjectionMembers
        {
            First = 11,
            Ignored = probe.Value,
            Last = 29,
        };

        Expression<Func<ProjectionMembers, int>> selector = row => row.Last;
        var initializer = Assert.IsType<MemberInitExpression>(projection.Body);
        var expected = Assert.IsType<MemberAssignment>(initializer.Bindings[2]).Expression;

        // Act
        var actual = NestedSetQueryExpressions.Inline(selector, projection.Body);

        // Assert
        Assert.Same(expected, actual);
        Assert.Equal(0, probe.Reads);
    }

    /// <summary>Fixed bindings preserve the general explicit-array overload's selected expression.</summary>
    /// <param name="arity">The number of independently supplied bindings.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Inline_FixedBindings_RetainGeneralOverloadResult(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity);
        var expression = Expression.Lambda(parameters[^1], parameters);
        var arguments = Enumerable.Range(1, arity).Select(value => (Expression)Expression.Constant(value)).ToArray();

        // Act
        var general = NestedSetQueryExpressions.Inline(expression, arguments);
        var fixedResult = Inline(expression, arguments);

        // Assert
        Assert.Same(arguments[^1], general);
        Assert.Same(general, fixedResult);
    }
}
