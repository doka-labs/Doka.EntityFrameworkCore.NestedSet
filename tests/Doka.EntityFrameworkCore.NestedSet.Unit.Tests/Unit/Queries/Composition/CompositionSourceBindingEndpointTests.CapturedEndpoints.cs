using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionSourceBindingEndpointTests
{
    /// <summary>
    /// An opaque query carrier cannot suppress bound-root membership or invoke an application getter.
    /// </summary>
    /// <param name="carrier">The field or property hiding the payload query behind an object type.</param>
    /// <param name="conversion">The explicit cast, type-as conversion or query factory used by the payload.</param>
    [Theory]
    [InlineData("field", "cast")]
    [InlineData("field", "type-as")]
    [InlineData("field", "as-queryable")]
    [InlineData("property", "cast")]
    [InlineData("property", "type-as")]
    [InlineData("property", "as-queryable")]
    [InlineData("static-field", "cast")]
    [InlineData("static-field", "type-as")]
    [InlineData("static-field", "as-queryable")]
    public void Require_ObjectCapturedPayload_DoesNotBorrowBoundVisibility(
        string carrier,
        string conversion
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context
            .NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1)
            .InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var payloads = context
            .Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping)
            .IgnoreQueryFilters();

        var holder = new ObjectQueryCapture(payloads);
        var capture = carrier switch
        {
            "field" => (Expression)Expression.Field(Expression.Constant(holder), nameof(ObjectQueryCapture.Query)),
            "property" => Expression.Property(Expression.Constant(holder), nameof(ObjectQueryCapture.QueryProperty)),
            "static-field" => Expression.Field(
                null,
                typeof(ObjectQueryCapture),
                nameof(ObjectQueryCapture.StaticQuery)),
            _ => throw new ArgumentOutOfRangeException(nameof(carrier)),
        };

        Expression<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>> asQueryable =
            rows => rows.AsQueryable();

        var converted = conversion switch
        {
            "cast" => (Expression)Expression.Convert(capture, typeof(IQueryable<CompositionGuardNode>)),
            "type-as" => Expression.TypeAs(capture, typeof(IQueryable<CompositionGuardNode>)),
            "as-queryable" => Expression.Call(
                ((MethodCallExpression)asQueryable.Body).Method,
                Expression.Convert(capture, typeof(IEnumerable<CompositionGuardNode>))),
            _ => throw new ArgumentOutOfRangeException(nameof(conversion)),
        };

        var captured = nodes.Provider.CreateQuery<CompositionGuardNode>(converted);
        var parameter = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "selected");
        var selected = nodes.Provider.CreateQuery<CompositionGuardNode>(parameter);
        var composed = selected.Join(
            captured,
            node => node.Id,
            payload => payload.Id,
            (node, payload) => node);

        var compose = Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(
            composed.Expression,
            parameter);

        var query = Bind(nodes, compose);
        var mapping = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];
        ObjectQueryCapture.StaticQuery = payloads;

        try
        {
            // WHY: Plain EF translation verifies both conversion forms without calling the property getter.
            // The guard below must not need to execute that getter to reject its opaque query carrier.
            if (carrier != "property")
            {
                _ = nodes
                    .Join(
                        captured,
                        node => node.Id,
                        payload => payload.Id,
                        (node, payload) => node.Id)
                    .ToQueryString();
            }

            // Act
            var actual = NestedSetQueryEndpoint.Require(query, node => node, mapping, bound, "node", s_binding);

            // Assert
            Assert.Same(mapping, actual.EntityType);
            Assert.False(actual.IsVisible);
            Assert.Equal(0, holder.GetterReads);
            Assert.Equal(0, boundary.ConnectionStarts);
            Assert.Equal(0, boundary.Commands.Created);
        }
        finally
        {
            ObjectQueryCapture.StaticQuery = null;
        }
    }

    /// <summary>An opaque property cannot establish endpoint provenance by executing its application getter.</summary>
    /// <param name="typeAs">Whether the property is converted using type-as rather than an explicit cast.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Require_ObjectProperty_CannotSupplyEndpointMapping(
        bool typeAs
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode>
            nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);

        var holder = new ObjectQueryCapture(nodes);
        var capture = Expression.Property(Expression.Constant(holder), nameof(ObjectQueryCapture.QueryProperty));
        var converted = typeAs
            ? Expression.TypeAs(capture, typeof(IQueryable<CompositionGuardNode>))
            : Expression.Convert(capture, typeof(IQueryable<CompositionGuardNode>));

        var captured = nodes.Provider.CreateQuery<CompositionGuardNode>(converted);
        var query = Bind(captured, selected => selected);
        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var error = Record.Exception(() => NestedSetQueryEndpoint.Require(
            query,
            node => node,
            mapping,
            nodes.Expression,
            "node",
            s_binding));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, holder.GetterReads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }
}
