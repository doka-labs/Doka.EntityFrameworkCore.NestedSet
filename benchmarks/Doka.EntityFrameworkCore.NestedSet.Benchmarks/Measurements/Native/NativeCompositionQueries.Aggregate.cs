namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

internal static partial class NativeCompositionQueries
{
    /// <summary>Builds an anchor-payload aggregate using all three filters on the same composed source.</summary>
    internal static IQueryable<NativeCompositionTotal> Aggregate(
        NativeCustomContext context,
        int firstAnchor,
        int firstFact
    )
    {
        var hierarchy = context
            .NestedSet<NativeCustomNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var pairs = hierarchy.SelectSubtrees(
            anchor => anchor.NodeId == 1 || anchor.NodeId >= firstAnchor,
            (anchor, node, distance) => new
            {
                Anchor = anchor,
                Node = node,
                Distance = distance,
            });

        var payload = context
            .Set<NativeCustomMarker>()
            .Where(fact => fact.Id >= firstFact);

        var contributions = pairs.Join(
            payload,
            row => row.Node.NodeId,
            fact => fact.Id,
            (row, fact) => new
            {
                row.Anchor,
                row.Node,
                row.Distance,
                FactKey = fact.Id,
            });

        var paths = hierarchy.WherePathEdges(
            contributions,
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

        return nearest
            .GroupBy(row => row.Anchor.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new NativeCompositionTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.FactKey)));
    }

}
