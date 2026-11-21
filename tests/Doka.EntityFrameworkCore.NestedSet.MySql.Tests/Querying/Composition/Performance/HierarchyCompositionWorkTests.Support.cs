namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public sealed partial class HierarchyCompositionWorkTests
{
    /// <summary>Builds the actual library path/ancestor filters after the requested subtree source.</summary>
    private static IQueryable<CompositionCapacityTotal> BuildPairs(
        TreeContext context,
        int firstAnchor,
        int firstFact,
        bool includeRoot,
        bool direct
    )
    {
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var pairs = hierarchy.SelectSubtrees(
            anchor => (includeRoot && anchor.NodeId == 1) || anchor.NodeId >= firstAnchor,
            (anchor, node, distance) => new Contribution
            {
                Anchor = anchor,
                Node = node,
                FactKey = node.NodeId,
            });

        var source = direct
            ? pairs.Where(row => row.Node.NodeId >= firstFact)
            : pairs.Join(
                context
                    .Set<UnrelatedRow>()
                    .Where(fact => fact.Id >= firstFact),
                row => row.Node.NodeId,
                fact => fact.Id,
                (row, fact) => new Contribution
                {
                    Anchor = row.Anchor,
                    Node = row.Node,
                    FactKey = fact.Id,
                });

        var paths = hierarchy.WherePathEdges(
            source,
            row => row.Anchor,
            row => row.Node,
            (_, parent, child) => child.Payload != "blocked");

        var any = hierarchy.WhereAnyAncestorOrSelf(
            paths,
            row => row.Node,
            (_, owner) => owner.Payload != "blocked");

        var nearest = hierarchy.WhereNearestAncestorOrSelf(
            any,
            row => row.Node,
            (_, owner) => owner.Depth >= 0,
            (_, owner) => owner.Payload != "blocked");

        return Totals(nearest);
    }

    /// <summary>Isolates path proof work using a bounded source with physically joined visible endpoints.</summary>
    private static IQueryable<CompositionCapacityTotal> BuildPath(
        TreeContext context,
        int firstAnchor
    )
    {
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var nodes = context
            .Set<TreeNode>()
            .Where(node => node.Tree == 1 && node.TreeId == Guid.Empty);

        var anchors = nodes.Where(node => node.NodeId >= firstAnchor && node.NodeId < firstAnchor + 10);
        var payload = context
            .Set<UnrelatedRow>()
            .Where(fact => fact.Id >= Nodes - 9);

        var source = anchors
            .Join(
                payload,
                _ => 1,
                _ => 1,
                (anchor, fact) => new
                {
                    Anchor = anchor,
                    Fact = fact,
                })
            .Join(
                nodes,
                row => row.Fact.Id,
                node => node.NodeId,
                (row, node) => new Contribution
                {
                    Anchor = row.Anchor,
                    Node = node,
                    FactKey = row.Fact.Id,
                });

        var paths = hierarchy.WherePathEdges(
            source,
            row => row.Anchor,
            row => row.Node,
            (_, parent, child) => child.Payload != "blocked");

        return Totals(paths);
    }

    /// <summary>Returns only scalar totals; entity materialization cannot hide the server-side work.</summary>
    private static IQueryable<CompositionCapacityTotal> Totals(
        IQueryable<Contribution> source
    ) => source
        .GroupBy(row => row.Anchor.NodeId)
        .OrderBy(group => group.Key)
        .Select(group => new CompositionCapacityTotal(
            group.Key,
            group.LongCount(),
            group.Sum(row => (long)row.FactKey)));
}
