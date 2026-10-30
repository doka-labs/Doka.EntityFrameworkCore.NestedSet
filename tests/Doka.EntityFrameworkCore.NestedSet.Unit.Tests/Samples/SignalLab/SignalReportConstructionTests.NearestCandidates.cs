using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportConstructionTests
{
    /// <summary>Each nearest group retains its own classification value without capturing another request.</summary>
    /// <param name="sameType">Whether two independently built candidates request the same classification.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearestCandidatesRetainIndependentClassificationSlots(bool sameType)
    {
        // Arrange
        var factory = typeof(SignalReportBuilder)
            .GetMethod("NearestOwnerCandidate", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<Guid, LambdaExpression>>();

        var firstType = Guid.NewGuid();
        var secondType = sameType ? firstType : Guid.NewGuid();
        var first = factory(firstType);
        var second = factory(secondType);
        var firstNode = new SignalNode { TypeId = firstType };
        var secondNode = new SignalNode { TypeId = secondType };
        var otherNode = new SignalNode { TypeId = Guid.NewGuid() };

        // WHY: The candidate must depend only on its owner. Omitting the row parameter here also rejects
        // accidental free row references while executing the actual expression instead of a copied predicate.
        var firstMatches = Expression.Lambda<Func<SignalNode, bool>>(first.Body, first.Parameters[1]).Compile();
        var secondMatches = Expression.Lambda<Func<SignalNode, bool>>(second.Body, second.Parameters[1]).Compile();

        // Act
        var actual = new[]
        {
            firstMatches(firstNode), firstMatches(secondNode), firstMatches(otherNode),
            secondMatches(firstNode), secondMatches(secondNode), secondMatches(otherNode),
        };

        // Assert
        Assert.Equal([true, sameType, false, sameType, true, false], actual);
        var firstSlot = Assert.IsAssignableFrom<MemberExpression>(Assert.IsAssignableFrom<BinaryExpression>(first.Body).Right);
        var secondSlot = Assert.IsAssignableFrom<MemberExpression>(Assert.IsAssignableFrom<BinaryExpression>(second.Body).Right);
        Assert.NotSame(Assert.IsType<ConstantExpression>(firstSlot.Expression).Value,
            Assert.IsType<ConstantExpression>(secondSlot.Expression).Value);
        Assert.NotSame(first, second);
    }
}
