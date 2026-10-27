namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks arguments and scope binding before database execution.</summary>
public sealed class CompositionArgumentTests
{
    /// <summary>Every required expression or query argument is validated before any I/O.</summary>
    /// <param name="argument">The public argument deliberately set to null.</param>
    /// <param name="expectedParameter">The documented public parameter name.</param>
    [Theory]
    [InlineData("ancestors.nodes", "nodes")]
    [InlineData("ancestors.projection", "projection")]
    [InlineData("ancestors-or-self.nodes", "nodes")]
    [InlineData("ancestors-or-self.projection", "projection")]
    [InlineData("subtrees.anchors", "anchors")]
    [InlineData("subtrees.projection", "projection")]
    [InlineData("path.source", "source")]
    [InlineData("path.anchor", "anchor")]
    [InlineData("path.node", "node")]
    [InlineData("path.edgeAllowed", "edgeAllowed")]
    [InlineData("any.source", "source")]
    [InlineData("any.node", "node")]
    [InlineData("any.predicate", "predicate")]
    [InlineData("nearest.source", "source")]
    [InlineData("nearest.node", "node")]
    [InlineData("nearest.candidate", "candidate")]
    [InlineData("nearest.predicate", "predicate")]
    public void RequiredArgumentsAreRejectedBeforeIo(
        string argument,
        string expectedParameter
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var source = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping)
            .Select(node => new CompositionGuardRow { Upper = node, Lower = node });

        // Act
        var error = Record.Exception(() => WithNullArgument(hierarchy, source, argument));

        // Assert
        Assert.Equal(expectedParameter, Assert.IsType<ArgumentNullException>(error).ParamName);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>None of the six composers can read a scoped hierarchy before its scope is bound explicitly.</summary>
    /// <param name="operation">The public method invoked before ForScope.</param>
    [Theory]
    [InlineData("ancestors")]
    [InlineData("ancestors-or-self")]
    [InlineData("subtrees")]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void ScopedHierarchyRequiresBindingForEveryComposer(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping);
        var source = context.Set<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .Select(node => new CompositionGuardRow { Upper = node, Lower = node });

        // Act
        var error = Record.Exception(() => operation switch
        {
            "ancestors" => (IQueryable)hierarchy.SelectAncestors(node => true, (node, ancestor, distance) => distance),
            "ancestors-or-self" => hierarchy.SelectAncestorsOrSelf(
                node => true, (node, ancestor, distance) => distance),
            "subtrees" => hierarchy.SelectSubtrees(node => true, (node, descendant, distance) => distance),
            _ => CompositionEndpointTests.Filter(hierarchy, source, operation),
        });

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Supplies one null public argument while keeping all unrelated arguments valid.</summary>
    private static IQueryable WithNullArgument(
        NestedSet<CompositionGuardNode> hierarchy,
        IQueryable<CompositionGuardRow> source,
        string argument
    ) => argument switch
    {
        "ancestors.nodes" => hierarchy.SelectAncestors<int>(null!, (node, ancestor, distance) => distance),
        "ancestors.projection" => hierarchy.SelectAncestors<int>(node => true, null!),
        "ancestors-or-self.nodes" => hierarchy.SelectAncestorsOrSelf<int>(
            null!, (node, ancestor, distance) => distance),
        "ancestors-or-self.projection" => hierarchy.SelectAncestorsOrSelf<int>(node => true, null!),
        "subtrees.anchors" => hierarchy.SelectSubtrees<int>(null!, (node, descendant, distance) => distance),
        "subtrees.projection" => hierarchy.SelectSubtrees<int>(node => true, null!),
        "path.source" => hierarchy.WherePathEdges<CompositionGuardRow>(
            null!, row => row.Upper, row => row.Lower, (row, parent, child) => true),
        "path.anchor" => hierarchy.WherePathEdges(source, null!, row => row.Lower, (row, parent, child) => true),
        "path.node" => hierarchy.WherePathEdges(source, row => row.Upper, null!, (row, parent, child) => true),
        "path.edgeAllowed" => hierarchy.WherePathEdges(source, row => row.Upper, row => row.Lower, null!),
        "any.source" => hierarchy.WhereAnyAncestorOrSelf<CompositionGuardRow>(
            null!, row => row.Lower, (row, owner) => true),
        "any.node" => hierarchy.WhereAnyAncestorOrSelf(source, null!, (row, owner) => true),
        "any.predicate" => hierarchy.WhereAnyAncestorOrSelf(source, row => row.Lower, null!),
        "nearest.source" => hierarchy.WhereNearestAncestorOrSelf<CompositionGuardRow>(null!, row => row.Lower,
            (row, owner) => true, (row, owner) => true),
        "nearest.node" => hierarchy.WhereNearestAncestorOrSelf(
            source, null!, (row, owner) => true, (row, owner) => true),
        "nearest.candidate" => hierarchy.WhereNearestAncestorOrSelf(
            source, row => row.Lower, null!, (row, owner) => true),
        "nearest.predicate" => hierarchy.WhereNearestAncestorOrSelf(
            source, row => row.Lower, (row, owner) => true, null!),
        _ => throw new ArgumentOutOfRangeException(nameof(argument)),
    };
}
