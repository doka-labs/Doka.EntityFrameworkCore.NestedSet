namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies generic composition against EF identity and property-mapping variants.</summary>
[Collection("Model compatibility")]
public abstract partial class HierarchyCompositionModelTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;
    private static int s_identity = 200_000;

    /// <summary>Uses the existing collection-owned model database for the current engine.</summary>
    protected HierarchyCompositionModelTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Allocates disjoint keys for unscoped models shared with existing compatibility suites.</summary>
    private static int NextIdentity() => Interlocked.Add(ref s_identity, 10);

    /// <summary>Exercises one public composition contract without loading endpoint entities.</summary>
    /// <remarks>
    /// The explicit mapped depth name also covers shadow and field access; it is never a CLR reflection reader.
    /// </remarks>
    private static IQueryable<int> QueryDepths<TEntity, TTreeId>(
        NestedSetTree<TEntity, TTreeId> hierarchy,
        HierarchyCompositionOperation operation,
        string depthProperty = nameof(INestedSetNode<int, Guid>.Depth)
    )
        where TEntity : class
        where TTreeId : notnull
    {
        var pairs = hierarchy.SelectSubtrees(
                node => EF.Property<int>(node, depthProperty) == 0,
                (anchor, node, distance) => new { Anchor = anchor, Node = node })
            .Where(row => EF.Property<int>(row.Node, depthProperty) == 2);

        // WHY: One operation per theory case isolates translation failures and keeps a single Act/Assert boundary.
        return operation switch
        {
            HierarchyCompositionOperation.Ancestors => hierarchy.SelectAncestors(
                node => EF.Property<int>(node, depthProperty) == 2,
                (node, owner, distance) => EF.Property<int>(owner, depthProperty)),
            HierarchyCompositionOperation.AncestorsOrSelf => hierarchy.SelectAncestorsOrSelf(
                node => EF.Property<int>(node, depthProperty) == 2,
                (node, owner, distance) => EF.Property<int>(owner, depthProperty)),
            HierarchyCompositionOperation.Subtrees => hierarchy.SelectSubtrees(
                node => EF.Property<int>(node, depthProperty) == 0,
                (anchor, node, distance) => EF.Property<int>(node, depthProperty)),
            HierarchyCompositionOperation.AllowedPath => hierarchy.WherePathEdges(
                    pairs,
                    row => row.Anchor,
                    row => row.Node,
                    (row, parent, child) => EF.Property<int>(child, depthProperty)
                        == EF.Property<int>(parent, depthProperty) + 1)
                .Select(row => EF.Property<int>(row.Node, depthProperty)),
            HierarchyCompositionOperation.AnyAncestor => hierarchy.WhereAnyAncestorOrSelf(
                    pairs,
                    row => row.Node,
                    (row, owner) => EF.Property<int>(owner, depthProperty) == 0)
                .Select(row => EF.Property<int>(row.Node, depthProperty)),
            HierarchyCompositionOperation.NearestAncestor => hierarchy.WhereNearestAncestorOrSelf(
                    pairs,
                    row => row.Node,
                    (row, owner) => EF.Property<int>(owner, depthProperty) < 2,
                    (row, owner) => EF.Property<int>(owner, depthProperty) == 1)
                .Select(row => EF.Property<int>(row.Node, depthProperty)),
            HierarchyCompositionOperation.MissingNearestOwner => hierarchy.WhereNearestAncestorOrSelf(
                    pairs,
                    row => row.Node,
                    (row, owner) => EF.Property<int>(owner, depthProperty) == 99,
                    (row, owner) => EF.Property<int>(owner, depthProperty) == 0)
                .Select(row => EF.Property<int>(row.Node, depthProperty)),
            HierarchyCompositionOperation.NearestOwnerRejectsValue => hierarchy.WhereNearestAncestorOrSelf(
                    pairs,
                    row => row.Node,
                    (row, owner) => EF.Property<int>(owner, depthProperty) < 2,
                    (row, owner) => EF.Property<int>(owner, depthProperty) == 0)
                .Select(row => EF.Property<int>(row.Node, depthProperty)),
            HierarchyCompositionOperation.BlockedPath => hierarchy.WherePathEdges(
                    pairs,
                    row => row.Anchor,
                    row => row.Node,
                    (row, parent, child) => EF.Property<int>(child, depthProperty) != 1)
                .Select(row => EF.Property<int>(row.Node, depthProperty)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    /// <summary>Checks the independent three-node parent-list oracle and the query's I/O boundary.</summary>
    private static void AssertDepths(
        HierarchyCompositionOperation operation,
        int[] actual,
        CompositionCommandProbe probe,
        DbContext context,
        int materialized
    )
    {
        int[] expected = operation switch
        {
            HierarchyCompositionOperation.Ancestors => [0, 1],
            HierarchyCompositionOperation.AncestorsOrSelf or HierarchyCompositionOperation.Subtrees => [0, 1, 2],
            HierarchyCompositionOperation.AllowedPath or HierarchyCompositionOperation.AnyAncestor
                or HierarchyCompositionOperation.NearestAncestor => [2],
            HierarchyCompositionOperation.MissingNearestOwner or HierarchyCompositionOperation.NearestOwnerRejectsValue
                or HierarchyCompositionOperation.BlockedPath => [],
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Assert.Equal(expected, actual);
        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, materialized);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
