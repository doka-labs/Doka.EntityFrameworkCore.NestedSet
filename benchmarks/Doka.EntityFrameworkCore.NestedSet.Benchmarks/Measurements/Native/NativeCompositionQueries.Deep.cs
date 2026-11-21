namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

internal static partial class NativeCompositionQueries
{
    /// <summary>Builds the complete deep path and nearest-candidate aggregate without any provider execution.</summary>
    internal static IQueryable<NativeCompositionTotal> Deep(
        NativeCustomContext context,
        int depth,
        int facts,
        int anchors,
        string shape
    )
    {
        var hierarchy = context
            .NestedSet<NativeCustomNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var lastFact = depth + 1;
        var firstFact = lastFact - facts + 1;
        var pairs = hierarchy.SelectSubtrees(
            anchor => anchor.NodeId <= anchors,
            (anchor, node, distance) => new
            {
                Anchor = anchor,
                Node = node,
                Distance = distance,
            });

        var payload = context
            .Set<NativeCustomMarker>()
            .Where(row => row.Id >= firstFact && row.Id <= lastFact);

        var source = pairs.Join(
            payload,
            row => row.Node.NodeId,
            row => row.Id,
            (row, fact) => new
            {
                row.Anchor,
                row.Node,
                row.Distance,
                FactKey = fact.Id,
            });

        var paths = shape is "Path" or "Overlap"
            ? hierarchy.WherePathEdges(
                source,
                row => row.Anchor,
                row => row.Node,
                (_, parent, child) => child.Payload != "blocked")
            : source;

        var strict = shape is "NearestStrictAccept" or "NearestStrictReject";
        var reject = shape is "NearestReject" or "NearestStrictReject";
        var filtered = shape == "Path"
            ? paths
            : hierarchy.WhereNearestAncestorOrSelf(
                paths,
                row => row.Node,
                (row, owner) => (shape == "RootOwner"
                        ? owner.NodeId == 1
                        : owner.Depth >= 0)
                    && (!strict || owner.NodeId != row.Node.NodeId),
                (row, owner) => shape == "NearestStrictAccept"
                    ? owner.NodeId == row.Node.Parent
                    : !reject || owner.NodeId == 1);

        var query = filtered
            .GroupBy(row => row.Anchor.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new NativeCompositionTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.FactKey)));

        return query;
    }

}
