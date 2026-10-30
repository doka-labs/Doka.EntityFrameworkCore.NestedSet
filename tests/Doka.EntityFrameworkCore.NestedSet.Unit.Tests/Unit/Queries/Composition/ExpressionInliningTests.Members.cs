using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ExpressionInliningTests
{
    /// <summary>Member lookup reaches both ends of a projection without evaluating unrelated getters.</summary>
    /// <param name="last">Whether to select the last assignment rather than the first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberInitializerSelectsAssignmentWithoutEvaluatingOtherMembers(
        bool last
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

        Expression<Func<ProjectionMembers, int>> selector = last ? row => row.Last : row => row.First;
        var initializer = Assert.IsType<MemberInitExpression>(projection.Body);
        var assignment = Assert.IsType<MemberAssignment>(initializer.Bindings[last ? 2 : 0]);

        // Act
        var actual = NestedSetQueryExpressions.Inline(selector, initializer);

        // Assert
        Assert.Same(assignment.Expression, actual);
        Assert.Equal(0, probe.Reads);
    }

    /// <summary>A member absent from an initializer retains a member access rather than another assignment.</summary>
    [Fact]
    public void MemberInitializerRetainsUnassignedMemberAccess()
    {
        // Arrange
        var probe = new GetterProbe();
        Expression<Func<ProjectionMembers>> projection = () => new ProjectionMembers
        {
            First = 11,
            Ignored = probe.Value,
            Last = 29,
        };

        Expression<Func<ProjectionMembers, int>> selector = row => row.Unassigned;

        // Act
        var actual = NestedSetQueryExpressions.Inline(selector, projection.Body);

        // Assert
        var member = Assert.IsType<MemberExpression>(actual, exactMatch: false);
        Assert.Equal(nameof(ProjectionMembers.Unassigned), member.Member.Name);
        Assert.Same(projection.Body, member.Expression);
        Assert.Equal(0, probe.Reads);
    }

    /// <summary>Provides distinct first, unrelated, last, and absent bindings for projection lookup.</summary>
    private sealed class ProjectionMembers
    {
        /// <summary>Gets the first assigned expression.</summary>
        public int First { get; init; }

        /// <summary>Gets an unrelated expression whose getter must not execute during composition.</summary>
        public int Ignored { get; init; }

        /// <summary>Gets the last assigned expression.</summary>
        public int Last { get; init; }

        /// <summary>Gets a member deliberately omitted from the initializer.</summary>
        public int Unassigned { get; init; }
    }
}
