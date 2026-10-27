namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks endpoint validation for actual relational join overloads with and without CLR comparers.</summary>
public sealed class CompositionComparerTests
{
    /// <summary>Comparer overloads fail with the documented argument error before any database operation.</summary>
    /// <param name="join">The actual Queryable join overload carrying the mapped endpoint.</param>
    /// <param name="operation">The composition filter validating the selected endpoint.</param>
    [Theory]
    [InlineData("join", "path")]
    [InlineData("join", "any")]
    [InlineData("join", "nearest")]
    [InlineData("group-join", "path")]
    [InlineData("group-join", "any")]
    [InlineData("group-join", "nearest")]
    [InlineData("left-join", "path")]
    [InlineData("left-join", "any")]
    [InlineData("left-join", "nearest")]
    [InlineData("right-join", "path")]
    [InlineData("right-join", "any")]
    [InlineData("right-join", "nearest")]
    public void ComparerJoinOverloadsRejectEndpointsBeforeIo(
        string join,
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var payloads = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = CreateJoin(nodes, payloads, join, useComparer: true);

        // Act
        var error = Record.Exception(() => CompositionEndpointTests.Filter(hierarchy, source, operation));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>The corresponding comparer-free overloads preserve the actual mapped endpoint alias.</summary>
    /// <param name="join">The actual Queryable join overload carrying the mapped endpoint.</param>
    /// <param name="operation">The composition filter validating the selected endpoint.</param>
    [Theory]
    [InlineData("join", "path")]
    [InlineData("join", "any")]
    [InlineData("join", "nearest")]
    [InlineData("group-join", "path")]
    [InlineData("group-join", "any")]
    [InlineData("group-join", "nearest")]
    [InlineData("left-join", "path")]
    [InlineData("left-join", "any")]
    [InlineData("left-join", "nearest")]
    [InlineData("right-join", "path")]
    [InlineData("right-join", "any")]
    [InlineData("right-join", "nearest")]
    public void OrdinaryJoinOverloadsRetainMappedEndpoints(
        string join,
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var nodes = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var payloads = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = CreateJoin(nodes, payloads, join, useComparer: false);

        // Act
        var query = CompositionEndpointTests.Filter(hierarchy, source, operation);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Same(source.Provider, query.Provider);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Calls real overloads while keeping endpoints on the guaranteed side of outer joins.</summary>
    private static IQueryable<CompositionGuardRow> CreateJoin(
        IQueryable<CompositionGuardNode> nodes,
        IQueryable<CompositionGuardNode> payloads,
        string join,
        bool useComparer
    )
    {
        // WHY: The default comparer still selects the comparer overload. A custom expression tree would not
        // catch accidental overload-position assumptions in the endpoint guard.
        var comparer = EqualityComparer<int>.Default;

        return (join, useComparer) switch
        {
            ("join", true) => nodes.Join(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }, comparer),
            ("join", false) => nodes.Join(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            ("group-join", true) => nodes.GroupJoin(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node }, comparer),
            ("group-join", false) => nodes.GroupJoin(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node }),
            ("left-join", true) => nodes.LeftJoin(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }, comparer),
            ("left-join", false) => nodes.LeftJoin(payloads, node => node.Id, payload => payload.Id,
                (node, payload) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            ("right-join", true) => payloads.RightJoin(nodes, payload => payload.Id, node => node.Id,
                (payload, node) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }, comparer),
            ("right-join", false) => payloads.RightJoin(nodes, payload => payload.Id, node => node.Id,
                (payload, node) => new CompositionGuardRow { Upper = node, Lower = node, Payload = payload }),
            _ => throw new ArgumentOutOfRangeException(nameof(join)),
        };
    }
}
