namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

internal static partial class NativeCompositionQueries
{
    /// <summary>Builds scalar actual public path and nearest-owner composition over physical endpoint rows.</summary>
    internal static IQueryable<NativeCompositionTotal> Dense(
        NativeCustomContext context,
        DenseConstructionCase scenario
    )
    {
        var hierarchy = context
            .NestedSet<NativeCustomNode>()
            .ForScope(1);

        var nodes = context
            .Set<NativeCustomNode>()
            .Where(node => node.Tree == 1 && (scenario.Forest || node.TreeId == Guid.Empty));

        IQueryable<Contribution> source;

        if (scenario.Branches)
        {
            var measured = context
                .Set<NativeCustomMarker>()
                .Join(
                    nodes,
                    fact => fact.Id,
                    node => node.NodeId,
                    (fact, node) => new
                    {
                        Fact = fact,
                        Node = node,
                    });

            source = scenario.Name == "RootAboveSiblingFailures"
                ? measured.Join(
                    nodes.Where(node => node.Parent == null),
                    row => row.Node.TreeId,
                    anchor => anchor.TreeId,
                    (row, anchor) => new Contribution
                    {
                        Anchor = anchor,
                        Node = row.Node,
                        FactKey = row.Fact.Id,
                    })
                : measured.Join(
                    nodes,
                    row => row.Node.NodeId - row.Node.Depth + 501,
                    anchor => anchor.NodeId,
                    (row, anchor) => new Contribution
                    {
                        Anchor = anchor,
                        Node = row.Node,
                        FactKey = row.Fact.Id,
                    });
        }
        else if (scenario.Forest)
        {
            var anchors = nodes.Where(node => node.Depth >= scenario.FirstAnchor - 1
                && node.Depth < scenario.FirstAnchor + 9);

            var measured = context
                .Set<NativeCustomMarker>()
                .Join(
                    nodes,
                    fact => fact.Id,
                    node => node.NodeId,
                    (fact, node) => new
                    {
                        Fact = fact,
                        Node = node,
                    })
                .Where(row => row.Node.Depth >= scenario.Nodes - 10);

            // WHY: Equal coordinates identify different physical nodes in each tree. Join source occurrences
            // by their explicit TreeId before applying the public forest operators, never as a cross product.
            source = anchors.Join(
                measured,
                anchor => anchor.TreeId,
                row => row.Node.TreeId,
                (anchor, row) => new Contribution
                {
                    Anchor = anchor,
                    Node = row.Node,
                    FactKey = row.Fact.Id,
                });
        }
        else
        {
            var anchors = nodes.Where(node => node.NodeId >= scenario.FirstAnchor
                && node.NodeId < scenario.FirstAnchor + 10);

            var payload = context
                .Set<NativeCustomMarker>()
                .Where(fact => fact.Id >= scenario.Nodes - 9);

            source = anchors
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
        }

        var paths = scenario.Forest
            ? hierarchy.WherePathEdges(
                source,
                row => row.Anchor,
                row => row.Node,
                (_, parent, child) => child.Payload != "blocked")
            : hierarchy
                .InTree(Guid.Empty)
                .WherePathEdges(
                    source,
                    row => row.Anchor,
                    row => row.Node,
                    (_, parent, child) => child.Payload != "blocked");

        if (scenario.Branches
            && scenario.Name != "RootAboveSiblingFailures")
        {
            // WHY: The marked upper endpoint is the nearest eligible structural owner in its branch.
            // Overlapping coordinates in the other tree and scope must never supply its path or owner.
            paths = scenario.Forest
                ? hierarchy.WhereNearestAncestorOrSelf(
                    paths,
                    row => row.Node,
                    (_, owner) => owner.Payload == "upper",
                    (row, owner) => owner.NodeId == row.Anchor.NodeId)
                : hierarchy
                    .InTree(Guid.Empty)
                    .WhereNearestAncestorOrSelf(
                        paths,
                        row => row.Node,
                        (_, owner) => owner.Payload == "upper",
                        (row, owner) => owner.NodeId == row.Anchor.NodeId);
        }

        if (scenario.CandidateCutoff is { } cutoff)
        {
            // WHY: Prior profiles leave every lower endpoint's own range empty, exercising the search over
            // earlier dense candidate ranges. Rejection must not fall back from the nearest candidate to root.
            if (scenario.Forest)
            {
                paths = scenario.Name == "DenseCandidatesPriorAccept"
                    ? hierarchy.WhereNearestAncestorOrSelf(
                        paths,
                        row => row.Node,
                        (_, owner) => owner.Depth < cutoff,
                        (_, owner) => owner.Depth == cutoff - 1)
                    : hierarchy.WhereNearestAncestorOrSelf(
                        paths,
                        row => row.Node,
                        (_, owner) => owner.Depth < cutoff,
                        (_, owner) => owner.Depth == 0);
            }
            else
            {
                paths = scenario.Name is "DenseCandidatesAccept" or "DenseCandidatesPriorAccept"
                    ? hierarchy
                        .InTree(Guid.Empty)
                        .WhereNearestAncestorOrSelf(
                            paths,
                            row => row.Node,
                            (_, owner) => owner.NodeId <= cutoff,
                            (_, owner) => owner.NodeId == cutoff)
                    : hierarchy
                        .InTree(Guid.Empty)
                        .WhereNearestAncestorOrSelf(
                            paths,
                            row => row.Node,
                            (_, owner) => owner.NodeId <= cutoff,
                            (_, owner) => owner.NodeId == 1);
            }
        }

        return paths
            .GroupBy(row => row.Anchor.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new NativeCompositionTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.FactKey)));
    }

    /// <summary>Keeps real endpoint aliases available until every public composition predicate is translated.</summary>
    private sealed class Contribution
    {
        /// <summary>Gets the selected upper endpoint.</summary>
        public required NativeCustomNode Anchor { get; init; }

        /// <summary>Gets the selected lower endpoint.</summary>
        public required NativeCustomNode Node { get; init; }

        /// <summary>Gets the joined payload key used by the scalar aggregate.</summary>
        public required int FactKey { get; init; }
    }
}
