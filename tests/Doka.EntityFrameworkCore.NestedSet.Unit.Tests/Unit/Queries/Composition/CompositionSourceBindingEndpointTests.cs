using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks lexical source-binding provenance independently of a provider's SQL lowering.</summary>
public sealed partial class CompositionSourceBindingEndpointTests
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
    public void Require_BoundAlias_RetainsMappingAndVisibility(
        string shape
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
        var query = shape switch
        {
            "identity" => Bind(nodes, selected => selected),
            "projection" => Bind(
                nodes,
                selected => selected
                    .Select(node => new { Node = node })
                    .Select(row => row.Node)),
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
    public void Require_InvalidBoundEndpoint_IsRejected(
        string shape
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode>
            first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);

        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var query = shape switch
        {
            "constructed" => Bind(
                first,
                selected => selected.Select(node => new CompositionGuardNode { Id = node.Id })),
            "foreign" => Bind(second, selected => selected),
            "untrusted" => Bind(first, selected => selected),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var mapping = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        var trusted = shape == "untrusted"
            ? null
            : s_binding;

        // Act
        var error = Record.Exception(() => NestedSetQueryEndpoint.Require(
            query,
            node => node,
            mapping,
            first.Expression,
            "node",
            trusted));

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
    public void Require_FilterModifiers_RemainVisibleToTheProof(
        string location
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

        var query = location switch
        {
            "input" => Bind(nodes.IgnoreQueryFilters(), selected => selected),
            "compose" => Bind(nodes, selected => selected.IgnoreQueryFilters()),
            "payload" => Bind(
                nodes,
                selected => selected.Join(
                    payloads,
                    node => node.Id,
                    payload => payload.Id,
                    (node, payload) => node)),
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

    /// <summary>A later independent input cannot inherit a previous binding's visibility.</summary>
    [Fact]
    public void Require_IndependentBindings_DoNotShareEndpointProof()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var tree = context
            .NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(1)
            .InTree(Guid.Empty);

        var nodes = tree.Nodes;
        var raw = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);
        var query = Bind(nodes, selected => selected)
            .Concat(Bind(raw, selected => selected));

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
    public void Require_NestedBinding_ResolvesInputBeforeShadowingItsParameter()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode>
            nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);

        var parameter = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "selected");
        var method = s_binding.MakeGenericMethod(typeof(CompositionGuardNode), typeof(CompositionGuardNode));
        var inner = Expression.Call(
            method,
            parameter,
            Expression.Quote(
                Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(
                    parameter,
                    parameter)));

        var outer = Expression.Call(
            method,
            nodes.Expression,
            Expression.Quote(
                Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(
                    inner,
                    parameter)));

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
    public void Require_UnboundQueryParameter_IsRejected()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        IQueryable<CompositionGuardNode>
            nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);

        var selected = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "selected");
        var unbound = Expression.Parameter(typeof(IQueryable<CompositionGuardNode>), "unbound");
        var method = s_binding.MakeGenericMethod(typeof(CompositionGuardNode), typeof(CompositionGuardNode));
        var expression = Expression.Call(
            method,
            nodes.Expression,
            Expression.Quote(
                Expression.Lambda<Func<IQueryable<CompositionGuardNode>, IQueryable<CompositionGuardNode>>>(
                    unbound,
                    selected)));

        var query = nodes.Provider.CreateQuery<CompositionGuardNode>(expression);
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
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Builds the trusted marker used solely to exercise the provider-independent guard.</summary>
    /// <remarks>
    /// The production call sites obtain their trusted method from the actual provider options assembly.
    /// </remarks>
    private static IQueryable<TResult> Bind<TSource, TResult>(
        IQueryable<TSource> source,
        Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose
    ) => source.Provider.CreateQuery<TResult>(
        Expression.Call(
            s_binding.MakeGenericMethod(typeof(TSource), typeof(TResult)),
            source.Expression,
            Expression.Quote(compose)));
}
