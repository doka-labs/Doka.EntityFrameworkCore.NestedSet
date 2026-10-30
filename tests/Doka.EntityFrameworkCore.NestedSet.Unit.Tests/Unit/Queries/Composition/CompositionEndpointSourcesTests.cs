using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks endpoint aliases from existing hierarchy queries and captured inner query sources.</summary>
public sealed class CompositionEndpointSourcesTests
{
    /// <summary>Existing key-based query methods remain valid sources for every new endpoint filter.</summary>
    /// <param name="sourceKind">The existing facade query or direct set supplying an actual mapped alias.</param>
    /// <param name="operation">The new composition filter applied to that alias.</param>
    [Theory]
    [InlineData("set", "path")]
    [InlineData("set", "any")]
    [InlineData("set", "nearest")]
    [InlineData("tree-containing", "path")]
    [InlineData("tree-containing", "any")]
    [InlineData("tree-containing", "nearest")]
    [InlineData("subtree", "path")]
    [InlineData("subtree", "any")]
    [InlineData("subtree", "nearest")]
    [InlineData("descendants", "path")]
    [InlineData("descendants", "any")]
    [InlineData("descendants", "nearest")]
    [InlineData("ancestors", "path")]
    [InlineData("ancestors", "any")]
    [InlineData("ancestors", "nearest")]
    [InlineData("children", "path")]
    [InlineData("children", "any")]
    [InlineData("children", "nearest")]
    [InlineData("parent", "path")]
    [InlineData("parent", "any")]
    [InlineData("parent", "nearest")]
    public void ExistingHierarchyQueriesRemainComposable(
        string sourceKind,
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var source = sourceKind switch
        {
            "set" => context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping),
            "tree-containing" => hierarchy.TreeContaining(1),
            "subtree" => hierarchy.SubtreeOf(1),
            "descendants" => hierarchy.DescendantsOf(1),
            "ancestors" => hierarchy.AncestorsOf(1),
            "children" => hierarchy.ChildrenOf(1),
            "parent" => hierarchy.ParentOf(1),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind)),
        };

        // Act
        var sql = Filter(hierarchy, source, node => node, operation).Select(node => node.Id).ToQueryString();

        // Assert
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(CompositionGuardContext.FirstMapping, sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A captured inner alias stays valid when the outer payload uses another named mapping.</summary>
    /// <param name="operation">The new composition filter applied to the selected inner alias.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void CapturedInnerAliasRetainsItsSelectedMapping(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = second.SelectMany(payload => first, (payload, node) => new { Node = node, Payload = payload });

        // Act
        var sql = Filter(hierarchy, source, row => row.Node, operation).Select(row => row.Node.Id).ToQueryString();

        // Assert
        Assert.Contains(CompositionGuardContext.FirstMapping, sql, StringComparison.Ordinal);
        Assert.Contains(CompositionGuardContext.SecondMapping, sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>The ordinary two-argument SelectMany overload preserves the actual inner mapped entity alias.</summary>
    /// <param name="operation">The new composition filter applied after the flattening query.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void TwoArgumentSelectManyRetainsInnerAlias(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = second.SelectMany(payload => first);

        // Act
        var sql = Filter(hierarchy, source, node => node, operation).Select(node => node.Id).ToQueryString();

        // Assert
        Assert.Contains(CompositionGuardContext.FirstMapping, sql, StringComparison.Ordinal);
        Assert.Contains(CompositionGuardContext.SecondMapping, sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A captured inner alias cannot borrow the outer hierarchy's mapping identity.</summary>
    /// <param name="operation">The new composition filter whose selected inner alias is foreign.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void CapturedInnerAliasFromAnotherNamedMappingIsRejected(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = first.SelectMany(payload => second, (payload, node) => new { Node = node, Payload = payload });

        // Act
        var error = Record.Exception(() => Filter(hierarchy, source, row => row.Node, operation));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A captured query does not turn a constructed result-selector entity into a mapped endpoint.</summary>
    /// <param name="operation">The new composition filter whose selected endpoint was constructed.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void CapturedQueryDoesNotLegitimizeConstructedEndpoints(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var source = second.SelectMany(payload => first,
            (payload, node) => new { Node = new CompositionGuardNode { Id = node.Id } });

        // Act
        var error = Record.Exception(() => Filter(hierarchy, source, row => row.Node, operation));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>The endpoint guard never invokes an application property getter to discover an inner query.</summary>
    /// <param name="operation">The new composition filter applied to a getter-backed inner query.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("any")]
    [InlineData("nearest")]
    public void QueryPropertyGetterIsNotExecutedByTheGuard(
        string operation
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        using var context = CompositionGuardContext.Create(boundary, boundary.Commands);
        var hierarchy = context.NestedSet<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var first = context.Set<CompositionGuardNode>(CompositionGuardContext.FirstMapping);
        var second = context.Set<CompositionGuardNode>(CompositionGuardContext.SecondMapping);
        var trap = new QueryGetterTrap(first);
        var source = second.SelectMany(payload => trap.Query, (payload, node) => new { Node = node });

        // Act
        var error = Record.Exception(() => Filter(hierarchy, source, row => row.Node, operation));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, trap.Reads);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Applies each public filter directly to the actual selected entity alias.</summary>
    private static IQueryable<TRow> Filter<TRow>(
        NestedSet<CompositionGuardNode> hierarchy,
        IQueryable<TRow> source,
        Expression<Func<TRow, CompositionGuardNode>> endpoint,
        string operation
    ) => operation switch
    {
        "path" => hierarchy.WherePathEdges(source, endpoint, endpoint, (row, parent, child) => parent.Id != child.Id),
        "any" => hierarchy.WhereAnyAncestorOrSelf(source, endpoint, (row, owner) => owner.Id > 0),
        "nearest" => hierarchy.WhereNearestAncestorOrSelf(source, endpoint,
            (row, owner) => owner.Id > 0, (row, owner) => owner.Depth >= 0),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    /// <summary>Records application getter execution that must not occur during endpoint validation.</summary>
    private sealed class QueryGetterTrap
    {
        private readonly IQueryable<CompositionGuardNode> _query;

        /// <summary>Retains a valid query behind a deliberately observable application getter.</summary>
        internal QueryGetterTrap(
            IQueryable<CompositionGuardNode> query
        )
        {
            _query = query;
        }

        /// <summary>Gets the number of property accesses performed by the guard.</summary>
        internal int Reads { get; private set; }

        /// <summary>Gets a query while exposing any unwanted application-code execution.</summary>
        internal IQueryable<CompositionGuardNode> Query
        {
            get
            {
                Reads++;

                return _query;
            }
        }
    }
}
