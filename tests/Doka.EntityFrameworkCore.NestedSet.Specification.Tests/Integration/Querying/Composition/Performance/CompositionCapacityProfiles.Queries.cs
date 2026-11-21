namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionCapacityProfiles
{
    /// <summary>Builds an anchor-payload aggregate using all three filters on the same composed source.</summary>
    private static IQueryable<CompositionCapacityTotal> Build(
        TreeContext context,
        int firstAnchor,
        int firstFact
    )
    {
        var hierarchy = context
            .NestedSet<TreeNode>()
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
            .Set<UnrelatedRow>()
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
            .Select(group => new CompositionCapacityTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.FactKey)));
    }

    /// <summary>Measures warmed construction only; database population never becomes a CLR collection.</summary>
    private static long MeasureConstruction(
        TreeContext context,
        int firstAnchor,
        int firstFact
    ) => MeasureConstruction(() => Build(context, firstAnchor, firstFact));

    /// <summary>Measures a warmed query factory without translation, I/O, or row-dependent work.</summary>
    private static long MeasureConstruction(
        Func<IQueryable> build
    )
    {
        for (var iteration = 0; iteration < 8; iteration++)
        {
            GC.KeepAlive(build());
        }

        const int operations = 32;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < operations; iteration++)
        {
            GC.KeepAlive(build());
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / operations;
    }
}
