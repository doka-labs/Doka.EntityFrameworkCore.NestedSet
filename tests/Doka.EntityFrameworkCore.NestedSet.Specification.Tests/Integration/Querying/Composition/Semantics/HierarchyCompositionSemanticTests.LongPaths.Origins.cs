namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Long paths preserve origin/date bags while excluding endpoints outside the bound tree.</summary>
    /// <param name="filter">Whether edge criteria are independent, per-date, or structural nearest.</param>
    [Theory]
    [InlineData("independent")]
    [InlineData("correlated")]
    [InlineData("nearest")]
    public async Task WherePathEdges_LongPaths_KeepOriginBagsAndCompleteBoundIdentities(
        string filter
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var otherScope = Guid.NewGuid();
        var nodes = LongChain(scope, tree, 1, 6);
        nodes[2].ToDate = new DateOnly(2044, 10, 19);
        var foreignTree = LongChain(scope, Guid.NewGuid(), 101, 6);
        var foreignScope = LongChain(otherScope, tree, 1, 6);
        foreignTree[3].ParentId = null;
        foreignScope[3].ParentId = null;
        var all = nodes
            .Concat(foreignTree)
            .Concat(foreignScope)
            .ToArray();

        var probe = new CompositionCommandProbe();
        await using var context = await CreateLongPathContextAsync(
            scope,
            6,
            all,
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope)
            .InTree(tree);

        var localRows = LongRows(context, scope, 1, 2, 6);
        var anchors = context
            .Set<CompositionNode>()
            .Where(node => node.Scope == scope && node.Id <= 2);

        var foreign = context
            .Set<CompositionNode>()
            .Where(node =>
                (node.Scope == scope && node.Id == 106) || (node.Scope == otherScope && node.Id == 6));

        var foreignRows = anchors
            .Join(
                foreign,
                _ => 1,
                _ => 1,
                (upper, node) => new
                {
                    Upper = upper,
                    Node = node,
                })
            .Join(
                context
                    .Set<CompositionInputOne>()
                    .Where(fact => fact.Scope == scope),
                _ => 1,
                _ => 1,
                (pair, fact) => new LongPathRow
                {
                    Upper = pair.Upper,
                    Node = pair.Node,
                    Day = fact.Date,
                });

        var source = localRows.Concat(foreignRows);
        var rows = source.Concat(source);
        var expected = ExpectedLongRows(
                all,
                scope,
                tree,
                1,
                2,
                6,
                filter)
            .SelectMany(row => new[] { row, row })
            .OrderBy(row => row.Origin)
            .ThenBy(row => row.Day)
            .ToArray();

        // Act
        var query = filter switch
        {
            "independent" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen),
            "correlated" => hierarchy.WherePathEdges(
                rows,
                row => row.Upper,
                row => row.Node,
                (row, parent, child) => child.EdgeOpen
                    && child.FromDate <= row.Day
                    && (!child.ToDate.HasValue || row.Day <= child.ToDate.Value)),
            "nearest" => hierarchy.WhereNearestAncestorOrSelf(
                rows,
                row => row.Node,
                (row, owner) => owner.Candidate,
                (row, owner) => owner.Matches),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var actual = await ReadLongRowsAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, actual);
        AssertLongReadBoundary(context, probe);
    }
}
