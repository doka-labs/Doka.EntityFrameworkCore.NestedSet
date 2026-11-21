namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

internal static partial class NativeCompositionQueries
{
    /// <summary>Preserves the source and pivot while expanding only the requested leaf under that pivot.</summary>
    internal static IQueryable<NativeCompositionTotal> Mixed(
        NativeCustomContext context,
        int firstOrigin,
        int target
    )
    {
        var hierarchy = context
            .NestedSet<NativeCustomNode>()
            .ForScope(1)
            .InTree(Guid.Empty);

        var up = hierarchy
            .SelectAncestors(
                source => source.NodeId >= firstOrigin,
                (source, pivot, distance) => new
                {
                    Source = source,
                    Pivot = pivot,
                    UpDistance = distance,
                })
            .Where(row => row.UpDistance == 1);

        var down = hierarchy
            .SelectSubtrees(
                pivot => up.Any(row => row.Pivot == pivot),
                (pivot, node, distance) => new
                {
                    Pivot = pivot,
                    Target = node,
                    DownDistance = distance,
                })
            .Where(row => row.Target.NodeId == target);

        var mixed = up.Join(
            down,
            row => row.Pivot,
            row => row.Pivot,
            (first, second) => new
            {
                first.Source,
                first.Pivot,
                second.Target,
            });

        var allowedUp = hierarchy.WherePathEdges(
            mixed,
            row => row.Pivot,
            row => row.Source,
            (_, parent, child) => child.Payload != "blocked");

        var allowedBoth = hierarchy.WherePathEdges(
            allowedUp,
            row => row.Pivot,
            row => row.Target,
            (_, parent, child) => child.Payload != "blocked");

        return allowedBoth
            .GroupBy(row => row.Source.NodeId)
            .OrderBy(group => group.Key)
            .Select(group => new NativeCompositionTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.Target.NodeId)));
    }


}
