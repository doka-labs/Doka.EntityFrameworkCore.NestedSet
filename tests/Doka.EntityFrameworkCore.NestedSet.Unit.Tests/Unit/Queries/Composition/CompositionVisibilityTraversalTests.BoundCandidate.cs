using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionVisibilityTraversalTests
{
    /// <summary>Unknown syntax inside the candidate cannot turn a first safe visit into permanent proof.</summary>
    /// <param name="shape">The custom dispatch or metadata mechanism inside the selected bound candidate.</param>
    /// <param name="rejectSecondVisit">Whether the next inspection exposes an unsafe query modifier or type.</param>
    [Theory]
    [InlineData("visit-children", false)]
    [InlineData("visit-children", true)]
    [InlineData("accept", false)]
    [InlineData("accept", true)]
    [InlineData("method", false)]
    [InlineData("method", true)]
    [InlineData("member", false)]
    [InlineData("member", true)]
    public void OpaqueBoundCandidateRetainsSecondInspection(string shape, bool rejectSecondVisit)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var nodes = Nodes(context);
        var forbidden = Any(nodes.IgnoreQueryFilters().Expression);
        var second = rejectSecondVisit ? forbidden : Expression.Constant(true);
        var children = new ChangingPredicate(second);
        var dispatch = new ChangingDispatch(second);
        var method = new QueryMethodProbe(rejectSecondVisit);
        var member = new QueryMemberProbe(rejectSecondVisit);
        var holder = new MetadataHolder();
        var candidate = shape switch
        {
            "visit-children" => Where(nodes.Expression, children),
            "accept" => Where(nodes.Expression, dispatch),
            // WHY: Keep mutable metadata out of the selected alias chain so Resolve cannot consume
            // its first safe observation before the visibility proof inspects the repeated candidate.
            "method" => Where(nodes.Expression, Any(method.Where(nodes.Expression, Expression.Constant(true)))),
            "member" => Where(nodes.Expression, Expression.Property(Expression.Constant(holder), member)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var boundQuery = nodes.Provider.CreateQuery<CompositionGuardNode>(candidate);
        var source = Source(boundQuery, [candidate]);
        var entityType = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        method.Reset();
        member.Reset();

        // Act
        var actual = NestedSetQueryEndpoint.Require(source, node => node, entityType, candidate, "node");

        // Assert
        Assert.Equal(!rejectSecondVisit, actual.IsVisible);
        var inspections = shape switch
        {
            "visit-children" => children.Visits,
            "accept" => dispatch.Visits,
            "method" => method.DeclaringTypeReads,
            "member" => member.TypeReads,
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        Assert.InRange(inspections, 2, int.MaxValue);
        Assert.Equal(0, holder.GetterReads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }
}
