using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks lexical source-binding provenance independently of a provider's SQL lowering.</summary>
/// <param name="output">Records deterministic construction-allocation evidence.</param>
public sealed partial class CompositionSourceBindingEndpointTests(ITestOutputHelper output)
{
    private static readonly MethodInfo s_binding = typeof(CompositionSourceBindingEndpointTests)
        .GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Identity and projected endpoints retain their exact named mapping through a trusted binding.</summary>
    /// <param name="shape">The ordinary composition within the statement-local binding.</param>
    [Theory]
    [InlineData("identity")]
    [InlineData("projection")]
    [InlineData("nested")]
    [InlineData("as-queryable")]
    public void BoundAliasRetainsMappingAndVisibility(string shape)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var query = shape switch
        {
            "identity" => Bind(nodes, selected => selected),
            "projection" => Bind(nodes, selected => selected.Select(node => new { Node = node }).Select(row => row.Node)),
            "nested" => Bind(nodes, selected => Bind(selected.Where(node => node.Id > 0), inner => inner)),
            "as-queryable" => Bind(nodes, selected => selected.AsQueryable()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var mapping = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var result = NestedSetQueryEndpoint.Require(query, node => node, mapping, bound, "node", s_binding);

        // Assert
        Assert.Same(mapping, result.EntityType);
        Assert.True(result.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Bindings do not legitimize constructed entities or another named mapping.</summary>
    /// <param name="shape">The source whose apparent CLR identity must not establish mapping ownership.</param>
    [Theory]
    [InlineData("constructed")]
    [InlineData("foreign")]
    [InlineData("untrusted")]
    public void InvalidBoundEndpointIsRejected(string shape)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var query = shape switch
        {
            "constructed" => Bind(first, selected => selected.Select(node => new CompositionGuardNode { Id = node.Id })),
            "foreign" => Bind(second, selected => selected),
            "untrusted" => Bind(first, selected => selected),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        var trusted = shape == "untrusted" ? null : s_binding;

        // Act
        var error = Record.Exception(() => NestedSetQueryEndpoint.Require(
            query, node => node, mapping, first.Expression, "node", trusted));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Filter modifiers on either side of a binding prevent the visibility shortcut.</summary>
    /// <param name="location">The source leg that disables query filters.</param>
    [Theory]
    [InlineData("input")]
    [InlineData("compose")]
    [InlineData("payload")]
    public void FilterModifiersRemainVisibleToTheProof(string location)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var payloads = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping).IgnoreQueryFilters();
        var query = location switch
        {
            "input" => Bind(nodes.IgnoreQueryFilters(), selected => selected),
            "compose" => Bind(nodes, selected => selected.IgnoreQueryFilters()),
            "payload" => Bind(nodes, selected => selected.Join(payloads, node => node.Id,
                payload => payload.Id, (node, payload) => node)),
            _ => throw new ArgumentOutOfRangeException(nameof(location)),
        };

        var mapping = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var result = NestedSetQueryEndpoint.Require(query, node => node, mapping, bound, "node", s_binding);

        // Assert
        Assert.False(result.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>An opaque query carrier cannot suppress bound-root membership or invoke an application getter.</summary>
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
    public void ObjectCapturedPayloadDoesNotBorrowBoundVisibility(string carrier, string conversion)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var payloads = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping).IgnoreQueryFilters();
        var holder = new ObjectQueryCapture(payloads);
        var capture = carrier switch
        {
            "field" => (Expression)Expression.Field(Expression.Constant(holder), nameof(ObjectQueryCapture.Query)),
            "property" => Expression.Property(Expression.Constant(holder), nameof(ObjectQueryCapture.QueryProperty)),
            "static-field" => Expression.Field(null, typeof(ObjectQueryCapture), nameof(ObjectQueryCapture.StaticQuery)),
            _ => throw new ArgumentOutOfRangeException(nameof(carrier)),
        };

        Expression<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>> asQueryable =
            rows => rows.AsQueryable();

        var converted = conversion switch
        {
            "cast" => (Expression)Expression.Convert(capture, typeof(IQueryable<CompositionGuardNode>)),
            "type-as" => Expression.TypeAs(capture, typeof(IQueryable<CompositionGuardNode>)),
            "as-queryable" => Expression.Call(((MethodCallExpression)asQueryable.Body).Method,
                Expression.Convert(capture, typeof(IEnumerable<CompositionGuardNode>))),
            _ => throw new ArgumentOutOfRangeException(nameof(conversion)),
        };

        var captured = nodes.Provider.CreateQuery<CompositionGuardNode>(converted);
        var parameter = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "selected");
        var selected = nodes.Provider.CreateQuery<CompositionGuardNode>(parameter);
        var composed = selected.Join(captured, node => node.Id, payload => payload.Id, (node, payload) => node);
        var compose = Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(
            composed.Expression, parameter);

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
                _ = nodes.Join(captured, node => node.Id, payload => payload.Id, (node, payload) => node.Id).ToQueryString();
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
    public void ObjectPropertyCannotSupplyEndpointMapping(bool typeAs)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
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
            query, node => node, mapping, nodes.Expression, "node", s_binding));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, holder.GetterReads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A later independent input cannot inherit a previous binding's visibility.</summary>
    [Fact]
    public void IndependentBindingsDoNotShareEndpointProof()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1).InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var raw = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);
        var query = Bind(nodes, selected => selected).Concat(Bind(raw, selected => selected));
        var mapping = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        var bound = ((MethodCallExpression)nodes.Expression).Arguments[0];

        // Act
        var result = NestedSetQueryEndpoint.Require(query, node => node, mapping, bound, "node", s_binding);

        // Assert
        Assert.Same(mapping, result.EntityType);
        Assert.False(result.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Reused parameter objects still resolve an inner input in its outer lexical environment.</summary>
    [Fact]
    public void NestedBindingResolvesInputBeforeShadowingItsParameter()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var parameter = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "selected");
        var method = s_binding.MakeGenericMethod(typeof(CompositionGuardNode), typeof(CompositionGuardNode));
        var inner = Expression.Call(method, parameter, Expression.Quote(
            Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(parameter, parameter)));

        var outer = Expression.Call(method, nodes.Expression, Expression.Quote(
            Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(inner, parameter)));

        var query = nodes.Provider.CreateQuery<CompositionGuardNode>(outer);
        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var result = NestedSetQueryEndpoint.Require(query, node => node, mapping, nodes.Expression, "node", s_binding);

        // Assert
        Assert.Same(mapping, result.EntityType);
        Assert.True(result.IsVisible);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>An unrelated unbound query parameter has no mapping provenance.</summary>
    [Fact]
    public void UnboundQueryParameterIsRejected()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode> nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var selected = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "selected");
        var unbound = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "unbound");
        var method = s_binding.MakeGenericMethod(typeof(CompositionGuardNode), typeof(CompositionGuardNode));
        var expression = Expression.Call(method, nodes.Expression, Expression.Quote(
            Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(unbound, selected)));

        var query = nodes.Provider.CreateQuery<CompositionGuardNode>(expression);
        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;

        // Act
        var error = Record.Exception(() => NestedSetQueryEndpoint.Require(
            query, node => node, mapping, nodes.Expression, "node", s_binding));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Builds the trusted marker used solely to exercise the provider-independent guard.</summary>
    /// <remarks>The production call sites obtain their trusted method from the actual provider options assembly.</remarks>
    private static IQueryable<TResult> Bind<TSource, TResult>(
        IQueryable<TSource> source,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose
    ) => source.Provider.CreateQuery<TResult>(Expression.Call(
        s_binding.MakeGenericMethod(typeof(TSource), typeof(TResult)), source.Expression, Expression.Quote(compose)));
}
