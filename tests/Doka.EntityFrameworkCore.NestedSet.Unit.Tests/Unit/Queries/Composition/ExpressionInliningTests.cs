using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks fixed-arity substitutions against the retained general expression-inlining contract.</summary>
/// <param name="output">Records measured allocation observations from the companion tests.</param>
public sealed partial class ExpressionInliningTests(ITestOutputHelper output)
{
    /// <summary>Fixed bindings retain independent nested parameters even when their names match.</summary>
    /// <param name="arity">The number of outer bindings replaced together.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NestedParametersRemainIndependent(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity);
        var nested = Expression.Parameter(typeof(int), "value");
        var sum = parameters.Aggregate<ParameterExpression, Expression>(nested, Expression.Add);
        var expression = Expression.Lambda(Expression.Lambda<Func<int, int>>(sum, nested), parameters);
        var arguments = Enumerable.Range(1, arity).Select(value => Expression.Constant(value)).ToArray();

        // Act
        var actual = Assert.IsAssignableFrom<Expression<Func<int, int>>>(Inline(expression, arguments));
        var value = actual.Compile()(10);

        // Assert
        Assert.Equal(10 + ((arity * (arity + 1)) / 2), value);
        Assert.Same(nested, actual.Parameters[0]);
    }

    /// <summary>Supplied bindings are inserted once without recursively replacing another supplied parameter.</summary>
    /// <param name="arity">The number of simultaneous bindings.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SuppliedArgumentsAreNotRevisited(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity);
        var external = Expression.Parameter(typeof(int), "value");
        var arguments = parameters.Skip(1).Append(external).Cast<Expression>().ToArray();
        var expression = Expression.Lambda(parameters[0], parameters);

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(arguments[0], actual);
    }

    /// <summary>Anonymous-constructor and member-initializer projections retain ordinary member reduction.</summary>
    /// <param name="arity">The number of projection bindings.</param>
    /// <param name="initializer">Whether the projection uses a member initializer rather than a constructor.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void ProjectionMembersReduceToExistingArgumentExpressions(
        int arity,
        bool initializer
    )
    {
        // Arrange
        var (projection, selector) = initializer
            ? Projectors((int value) => new ProjectionRow { Value = value }, row => row.Value)
            : Projectors((int value) => new { Value = value }, row => row.Value);

        var parameters = Enumerable.Range(0, arity)
            .Select(_ => Expression.Parameter(projection.ReturnType, "row")).ToArray();

        var arguments = Enumerable.Range(1, arity)
            .Select(value => NestedSetQueryExpressions.Replace(
                projection.Body, projection.Parameters[0], Expression.Constant(value))).ToArray();

        var member = Assert.IsAssignableFrom<MemberExpression>(selector.Body).Member;
        var expression = Expression.Lambda(Expression.MakeMemberAccess(parameters[^1], member), parameters);

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Equal(arity, Assert.IsType<ConstantExpression>(actual).Value);
    }

    /// <summary>Captured application members remain unevaluated expression nodes.</summary>
    /// <param name="arity">The number of unused bindings supplied with the captured expression.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CapturedGettersAreNotInvoked(
        int arity
    )
    {
        // Arrange
        var probe = new GetterProbe();
        var member = Expression.Property(Expression.Constant(probe), nameof(GetterProbe.Value));
        var expression = Expression.Lambda(member, Parameters(arity));
        var arguments = Enumerable.Range(1, arity).Select(value => Expression.Constant(value)).ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(member, actual);
        Assert.Equal(0, probe.Reads);
    }

    /// <summary>Partial bindings still succeed when omitted parameters do not occur in the selected body.</summary>
    /// <param name="arity">The number of supplied bindings, one fewer than the lambda declares.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UnusedMissingBindingsRetainGeneralPathBehavior(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity + 1);
        var expression = Expression.Lambda(parameters[0], parameters);
        var arguments = Enumerable.Range(1, arity).Select(value => Expression.Constant(value)).ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(arguments[0], actual);
    }

    /// <summary>A required omitted binding retains the existing general-path exception.</summary>
    /// <param name="arity">The number of supplied bindings, one fewer than the lambda declares.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RequiredMissingBindingsRetainGeneralPathRejection(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity + 1);
        var expression = Expression.Lambda(parameters[^1], parameters);
        var arguments = Enumerable.Range(1, arity).Select(value => Expression.Constant(value)).ToArray();

        // Act
        var exception = Record.Exception(() => Inline(expression, arguments));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>Extra bindings remain ignored rather than imposing a new arity-validation contract.</summary>
    /// <param name="arity">The number of supplied bindings, one more than the lambda declares.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void UnusedAdditionalBindingsRetainGeneralPathBehavior(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity - 1);
        var expression = Expression.Lambda(parameters[0], parameters);
        var arguments = Enumerable.Range(1, arity).Select(value => Expression.Constant(value)).ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(arguments[0], actual);
    }

    /// <summary>Existing coordinate templates retain support for their real larger binding counts.</summary>
    /// <param name="arity">A binding count used by the existing nearest/path-failure templates.</param>
    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void GeneralPathRetainsLargerBindings(
        int arity
    )
    {
        // Arrange
        var parameters = Parameters(arity);
        var expression = Expression.Lambda(parameters[^1], parameters);
        var arguments = Enumerable.Range(1, arity).Select(value => Expression.Constant(value)).ToArray();

        // Act
        var actual = NestedSetQueryExpressions.Inline(expression, arguments);

        // Assert
        Assert.Same(arguments[^1], actual);
    }

    /// <summary>Dispatches each prepared binding count through the corresponding compile-time overload.</summary>
    private static Expression Inline(
        LambdaExpression expression,
        IReadOnlyList<Expression> arguments
    ) => arguments.Count switch
    {
        1 => NestedSetQueryExpressions.Inline(expression, arguments[0]),
        2 => NestedSetQueryExpressions.Inline(expression, arguments[0], arguments[1]),
        3 => NestedSetQueryExpressions.Inline(expression, arguments[0], arguments[1], arguments[2]),
        _ => throw new ArgumentOutOfRangeException(nameof(arguments)),
    };

    /// <summary>Creates distinct bindings with deliberately identical names.</summary>
    private static ParameterExpression[] Parameters(
        int arity
    ) => Enumerable.Range(0, arity).Select(_ => Expression.Parameter(typeof(int), "value")).ToArray();

    /// <summary>Preserves the anonymous projection type through generic inference.</summary>
    private static (LambdaExpression Projection, LambdaExpression Selector) Projectors<TProjection>(
        Expression<Func<int, TProjection>> projection,
        Expression<Func<TProjection, int>> selector
    ) => (projection, selector);

    /// <summary>Provides the member-initializer projection used by the binding controls.</summary>
    private sealed class ProjectionRow
    {
        /// <summary>Gets the projected value selected during inlining.</summary>
        public required int Value { get; init; }
    }

    /// <summary>Detects accidental evaluation of a captured application getter.</summary>
    private sealed class GetterProbe
    {
        /// <summary>Gets the number of unexpected application getter calls.</summary>
        internal int Reads { get; private set; }

        /// <summary>Gets a value only by recording a forbidden read and throwing.</summary>
        public int Value
        {
            get
            {
                Reads++;

                throw new InvalidOperationException("Captured getters must remain unevaluated.");
            }
        }
    }
}
