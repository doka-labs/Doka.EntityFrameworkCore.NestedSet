namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks Q44, U56 and C29 together: every composer builds deferred EF queries.</summary>
public sealed class CompositionDeferredTests
{
    private static readonly Guid s_treeId = Guid.Parse("041c7483-057b-4ca8-b5df-dace9ff97994");

    /// <summary>Each unscoped composition method constructs a query without starting database I/O.</summary>
    /// <param name="operation">The independent composition operation under test.</param>
    [Theory]
    [InlineData("Subtrees")]
    [InlineData("Ancestors")]
    [InlineData("AncestorsOrSelf")]
    [InlineData("Path")]
    [InlineData("Any")]
    [InlineData("Nearest")]
    public void ScopelessComposersBuildWithoutIo(string operation)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var source = hierarchy.SelectSubtrees(node => node.Id > 0,
            (upper, lower, distance) => new CompositionGuardRow { Upper = upper, Lower = lower, Distance = distance });

        // Act
        var query = Build(hierarchy, source, operation);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Binding a configured scope preserves deferred construction for all six composition methods.</summary>
    /// <param name="operation">The independent composition operation under test.</param>
    [Theory]
    [InlineData("Subtrees")]
    [InlineData("Ancestors")]
    [InlineData("AncestorsOrSelf")]
    [InlineData("Path")]
    [InlineData("Any")]
    [InlineData("Nearest")]
    public void ScopedComposersBuildWithoutIo(string operation)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context
            .NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(7);

        var source = hierarchy.SelectSubtrees(node => node.Id > 0,
            (upper, lower, distance) => new CompositionGuardRow { Upper = upper, Lower = lower, Distance = distance });

        // Act
        var query = Build(hierarchy, source, operation);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A tree-bound facade exposes the same deferred methods without loading its root or registry.</summary>
    /// <param name="operation">The independent composition operation under test.</param>
    [Theory]
    [InlineData("Subtrees")]
    [InlineData("Ancestors")]
    [InlineData("AncestorsOrSelf")]
    [InlineData("Path")]
    [InlineData("Any")]
    [InlineData("Nearest")]
    public void TreeBoundComposersBuildWithoutIo(string operation)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context
            .NestedSet<CompositionGuardNode>(CompositionGuardContext.ScopedMapping)
            .ForScope(7)
            .InTree(s_treeId);

        var source = hierarchy.SelectSubtrees(node => node.Id > 0,
            (upper, lower, distance) => new CompositionGuardRow { Upper = upper, Lower = lower, Distance = distance });

        // Act
        var query = Build(hierarchy, source, operation);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A composed upward-to-downward path stays deferred across independent pair queries.</summary>
    [Fact]
    public void MixedDirectionCompositionBuildsWithoutIo()
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var down = hierarchy.SelectSubtrees(node => node.Id > 0,
            (upper, lower, distance) => new CompositionGuardRow { Upper = upper, Lower = lower, Distance = distance });

        var up = hierarchy.SelectAncestors(node => node.Id > 0,
            (lower, upper, distance) => new CompositionGuardRow { Upper = upper, Lower = lower, Distance = distance });

        var mixed = up.Join(down, upward => upward.Upper.Id, downward => downward.Upper.Id,
            (upward, downward) => new CompositionGuardRow
            {
                Upper = upward.Upper,
                Lower = downward.Lower,
                Payload = upward.Lower,
            });

        // Act
        var query = hierarchy.WherePathEdges(
            mixed, row => row.Upper, row => row.Lower, (row, parent, child) => parent.Id != child.Id);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Constructs only the selected method so each failure belongs to one operation.</summary>
    private static IQueryable Build(
        NestedSet<CompositionGuardNode> hierarchy,
        IQueryable<CompositionGuardRow> source,
        string operation
    ) => operation switch
    {
        "Subtrees" => hierarchy.SelectSubtrees(node => node.Id > 0, (upper, lower, distance) => distance),
        "Ancestors" => hierarchy.SelectAncestors(node => node.Id > 0, (lower, upper, distance) => distance),
        "AncestorsOrSelf" => hierarchy.SelectAncestorsOrSelf(node => node.Id > 0, (node, owner, distance) => distance),
        "Path" => hierarchy.WherePathEdges(
            source, row => row.Upper, row => row.Lower, (row, parent, child) => parent.Id != child.Id),
        "Any" => hierarchy.WhereAnyAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Id == row.Upper.Id),
        "Nearest" => hierarchy.WhereNearestAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Depth == 1,
            (row, owner) => owner.Id == row.Upper.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    /// <summary>Constructs one scoped method using the prepared mapped endpoints.</summary>
    private static IQueryable Build(
        ScopedNestedSet<CompositionGuardNode, int> hierarchy,
        IQueryable<CompositionGuardRow> source,
        string operation
    ) => operation switch
    {
        "Subtrees" => hierarchy.SelectSubtrees(node => node.Id > 0, (upper, lower, distance) => distance),
        "Ancestors" => hierarchy.SelectAncestors(node => node.Id > 0, (lower, upper, distance) => distance),
        "AncestorsOrSelf" => hierarchy.SelectAncestorsOrSelf(node => node.Id > 0, (node, owner, distance) => distance),
        "Path" => hierarchy.WherePathEdges(
            source, row => row.Upper, row => row.Lower, (row, parent, child) => parent.Id != child.Id),
        "Any" => hierarchy.WhereAnyAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Id == row.Upper.Id),
        "Nearest" => hierarchy.WhereNearestAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Depth == 1,
            (row, owner) => owner.Id == row.Upper.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    /// <summary>Constructs one tree-bound method using the prepared mapped endpoints.</summary>
    private static IQueryable Build(
        NestedSetTree<CompositionGuardNode, Guid> hierarchy,
        IQueryable<CompositionGuardRow> source,
        string operation
    ) => operation switch
    {
        "Subtrees" => hierarchy.SelectSubtrees(node => node.Id > 0, (upper, lower, distance) => distance),
        "Ancestors" => hierarchy.SelectAncestors(node => node.Id > 0, (lower, upper, distance) => distance),
        "AncestorsOrSelf" => hierarchy.SelectAncestorsOrSelf(node => node.Id > 0, (node, owner, distance) => distance),
        "Path" => hierarchy.WherePathEdges(
            source, row => row.Upper, row => row.Lower, (row, parent, child) => parent.Id != child.Id),
        "Any" => hierarchy.WhereAnyAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Id == row.Upper.Id),
        "Nearest" => hierarchy.WhereNearestAncestorOrSelf(source, row => row.Lower, (row, owner) => owner.Depth == 1,
            (row, owner) => owner.Id == row.Upper.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
