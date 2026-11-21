using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ExpressionInliningTests
{
    /// <summary>Provides a static getter that expression composition must never invoke.</summary>
    private static int UnreadStaticValue =>
        throw new InvalidOperationException("Static getters must remain unevaluated.");

    /// <summary>Static and directly captured members retain their exact expression without visiting getters.</summary>
    /// <param name="arity">The fixed overload's binding count.</param>
    /// <param name="staticMember">Whether the selected getter is static instead of directly captured.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Inline_UnboundMembers_RetainExpressionWithoutGetterEvaluation(
        int arity,
        bool staticMember
    )
    {
        // Arrange
        var probe = new GetterProbe();
        var member = staticMember
            ? Expression.Property(
                null,
                typeof(ExpressionInliningTests).GetProperty(
                    nameof(UnreadStaticValue),
                    BindingFlags.NonPublic | BindingFlags.Static)!)
            : Expression.Property(Expression.Constant(probe), nameof(GetterProbe.Value));

        var expression = Expression.Lambda(member, Parameters(arity));
        var arguments = Enumerable
            .Repeat<Expression>(Expression.Constant(7), arity)
            .ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(member, actual);
        Assert.Equal(0, probe.Reads);
    }

    /// <summary>
    /// Every fixed binding slot returns the exact supplied expression without revisiting its parameters.
    /// </summary>
    /// <param name="arity">The fixed overload's binding count.</param>
    /// <param name="selected">The independently selected parameter slot.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void Inline_DirectIdentityBinding_ReusesEverySuppliedArgument(
        int arity,
        int selected
    )
    {
        // Arrange
        var parameters = Parameters(arity);
        var expression = Expression.Lambda(parameters[selected], parameters);
        var external = Expression.Parameter(typeof(int), "value");
        var arguments = parameters
            .Skip(1)
            .Append(external)
            .Cast<Expression>()
            .ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(arguments[selected], actual);
    }

    /// <summary>Direct member selection visits no unrelated projection value in any supplied binding slot.</summary>
    /// <param name="arity">The fixed overload's binding count.</param>
    /// <param name="selected">The selected projection's parameter slot.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void Inline_DirectMemberBinding_ReusesSelectedAssignment(
        int arity,
        int selected
    )
    {
        // Arrange
        var probe = new GetterProbe();
        Expression<Func<ProjectionMembers>> projection = () => new ProjectionMembers
        {
            First = 11,
            Ignored = probe.Value,
            Last = 29,
        };

        var parameters = Enumerable
            .Range(0, arity)
            .Select(_ => Expression.Parameter(typeof(ProjectionMembers), "row"))
            .ToArray();

        var expression = Expression.Lambda(
            Expression.Property(parameters[selected], nameof(ProjectionMembers.Last)),
            parameters);

        var initializer = Assert.IsType<MemberInitExpression>(projection.Body);
        var expected = Assert.IsType<MemberAssignment>(initializer.Bindings[2])
            .Expression;

        var arguments = Enumerable
            .Repeat<Expression>(initializer, arity)
            .ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(expected, actual);
        Assert.Equal(0, probe.Reads);
    }

    /// <summary>A direct-looking member on an unrelated parameter remains unbound despite an identical name.</summary>
    /// <param name="arity">The fixed overload's binding count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Inline_DirectMemberBinding_RetainsForeignParameterIdentity(
        int arity
    )
    {
        // Arrange
        var parameters = Enumerable
            .Range(0, arity)
            .Select(_ => Expression.Parameter(typeof(ProjectionMembers), "row"))
            .ToArray();

        var foreign = Expression.Parameter(typeof(ProjectionMembers), "row");
        var member = Expression.Property(foreign, nameof(ProjectionMembers.Last));
        var expression = Expression.Lambda(member, parameters);
        var arguments = Enumerable
            .Repeat<Expression>(Expression.New(typeof(ProjectionMembers)), arity)
            .ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(member, actual);
        Assert.Same(
            foreign,
            Assert.IsAssignableFrom<MemberExpression>(actual)
                .Expression);
    }

    /// <summary>Selected projection assignments retain any other lambda parameter they contain.</summary>
    /// <param name="arity">The fixed overload's binding count.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Inline_DirectMemberAssignment_DoesNotRevisitAnotherBinding(
        int arity
    )
    {
        // Arrange
        var first = Expression.Parameter(typeof(ProjectionMembers), "row");
        var remaining = Parameters(arity - 1);
        var parameters = new[] { first }
            .Concat(remaining)
            .ToArray();

        var property = typeof(ProjectionMembers).GetProperty(nameof(ProjectionMembers.First))!;
        var initializer = Expression.MemberInit(
            Expression.New(typeof(ProjectionMembers)),
            Expression.Bind(property, remaining[0]));

        var expression = Expression.Lambda(Expression.Property(first, property), parameters);
        var arguments = new Expression[] { initializer }
            .Concat(
                Enumerable
                    .Range(1, arity - 1)
                    .Select(value => Expression.Constant(value)))
            .ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(remaining[0], actual);
    }

    /// <summary>Invalid member targets retain the general visitor's exception type and parameter name.</summary>
    /// <param name="arity">The fixed overload's binding count.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Inline_DirectMemberBinding_RetainsInvalidTargetRejection(
        int arity
    )
    {
        // Arrange
        var parameters = Enumerable
            .Range(0, arity)
            .Select(_ => Expression.Parameter(typeof(ProjectionMembers), "row"))
            .ToArray();

        var expression = Expression.Lambda(
            Expression.Property(parameters[^1], nameof(ProjectionMembers.Last)),
            parameters);

        var arguments = Enumerable
            .Repeat<Expression>(Expression.Constant("wrong target"), arity)
            .ToArray();

        // Act
        var expected = Record.Exception(() => NestedSetQueryExpressions.Inline(expression, arguments));
        var actual = Record.Exception(() => Inline(expression, arguments));

        // Assert
        var expectedArgument = Assert.IsType<ArgumentException>(expected);
        var actualArgument = Assert.IsType<ArgumentException>(actual);
        Assert.Equal(expectedArgument.ParamName, actualArgument.ParamName);
    }

    /// <summary>Direct identity and existing-assignment selection retain the exact selected expression.</summary>
    /// <param name="arity">The fixed overload's binding count.</param>
    /// <param name="member">Whether to select a projection member rather than the complete supplied value.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Inline_DirectBinding_ReusesSelectedExpression(
        int arity,
        bool member
    )
    {
        // Arrange
        var parameters = Enumerable
            .Range(0, arity)
            .Select(_ => Expression.Parameter(
                member
                    ? typeof(ProjectionMembers)
                    : typeof(int),
                "row"))
            .ToArray();

        Expression body = member
            ? Expression.Property(parameters[^1], nameof(ProjectionMembers.Last))
            : parameters[^1];

        var expression = Expression.Lambda(body, parameters);
        var value = Expression.Constant(29);
        Expression argument = member
            ? Expression.MemberInit(
                Expression.New(typeof(ProjectionMembers)),
                Expression.Bind(typeof(ProjectionMembers).GetProperty(nameof(ProjectionMembers.Last))!, value))
            : value;

        var arguments = Enumerable
            .Repeat(argument, arity)
            .ToArray();

        // Act
        var actual = Inline(expression, arguments);

        // Assert
        Assert.Same(value, actual);
    }

}
