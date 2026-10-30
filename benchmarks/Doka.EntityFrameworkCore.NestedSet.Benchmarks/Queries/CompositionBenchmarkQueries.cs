namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Builds semantically identical scalar queries for API and explicit application-LINQ observations.</summary>
internal static class CompositionBenchmarkQueries
{
    /// <summary>Composes inclusive pairs, complete edges, existential owners, and nearest-owner filtering.</summary>
    internal static IQueryable<CompositionBenchmarkTotal> Api(
        BenchmarkContext context,
        Guid treeId,
        int lastKey,
        int anchors,
        int payloadRows
    )
    {
        var firstAnchor = Math.Max(2, lastKey - anchors + 2);
        var firstPayload = Math.Max(1, lastKey - payloadRows + 1);
        var tree = context
            .NestedSet<BenchmarkNode>()
            .ForScope(1)
            .InTree(treeId);

        var pairs = tree
            .SelectSubtrees(
                anchor => anchor.Id == 1 || anchor.Id >= firstAnchor,
                (anchor, node, distance) => new
                {
                    Anchor = anchor,
                    Node = node,
                })
            .Where(row => row.Node.Id >= firstPayload);

        var paths = tree.WherePathEdges(
            pairs,
            row => row.Anchor,
            row => row.Node,
            (_, parent, child) => child.Name != "blocked-edge");

        var any = tree.WhereAnyAncestorOrSelf(
            paths,
            row => row.Node,
            (_, owner) => owner.Payload != "blocked");

        var nearest = tree.WhereNearestAncestorOrSelf(
            any,
            row => row.Node,
            (_, owner) => owner.Depth >= 0,
            (_, owner) => owner.Payload != "blocked");

        return nearest
            .GroupBy(row => row.Anchor.Id)
            .OrderBy(group => group.Key)
            .Select(group => new CompositionBenchmarkTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.Node.Id)));
    }

    /// <summary>Expresses the same filter and bag contracts with ordinary application-owned EF LINQ.</summary>
    /// <remarks>
    /// WHY: Omitting path completeness or replacing Nearest with Any would benchmark a cheaper, different query.
    /// This explicit mapping-specific reference retains every owner and direct-edge condition of the API case.
    /// </remarks>
    internal static IQueryable<CompositionBenchmarkTotal> Reference(
        BenchmarkContext context,
        Guid treeId,
        int lastKey,
        int anchors,
        int payloadRows
    )
    {
        var firstAnchor = Math.Max(2, lastKey - anchors + 2);
        var firstPayload = Math.Max(1, lastKey - payloadRows + 1);
        var nodes = context
            .Nodes
            .AsNoTracking()
            .Where(node => node.Scope == 1 && node.TreeId == treeId);

        // WHY: Match the API's redundant finite windows on valid Left < Right intervals, so ordinary
        // LINQ can use either bounded coordinate index instead of timing an artificially weaker baseline.
        var pairs = nodes
            .Where(anchor => anchor.Id == 1 || anchor.Id >= firstAnchor)
            .SelectMany(
                anchor => nodes.Where(node =>
                    node.Id >= firstPayload
                    && node.Left >= anchor.Left
                    && node.Right <= anchor.Right
                    && node.Left < anchor.Right
                    && node.Right > anchor.Left),
                (anchor, node) => new
                {
                    Anchor = anchor,
                    Node = node,
                });

        var paths = pairs.Where(row => nodes.LongCount(child => child.Left > row.Anchor.Left
                && child.Left <= row.Node.Left
                && child.Right >= row.Node.Right
                && child.Right < row.Anchor.Right
                && child.Name != "blocked-edge"
                && nodes.Any(parent => parent.Id == child.ParentId
                    && (long)child.Depth == (long)parent.Depth + 1
                    && parent.Left >= row.Anchor.Left
                    && parent.Right <= row.Anchor.Right
                    && parent.Left < child.Left
                    && parent.Right > child.Right))
            == (long)row.Node.Depth - row.Anchor.Depth);

        var any = paths.Where(row => nodes.Any(owner => owner.Left <= row.Node.Left
            && owner.Right >= row.Node.Right
            && row.Node.Left < owner.Right
            && row.Node.Right > owner.Left
            && owner.Payload != "blocked"));

        var nearest = any.Where(row => nodes.Any(owner => owner.Id
            == nodes
                .Where(candidate => candidate.Left <= row.Node.Left
                    && candidate.Right >= row.Node.Right
                    && row.Node.Left < candidate.Right
                    && row.Node.Right > candidate.Left
                    && candidate.Depth >= 0)
                .OrderByDescending(candidate => candidate.Depth)
                .Select(candidate => (int?)candidate.Id)
                .FirstOrDefault()
            && owner.Payload != "blocked"
            && nodes.LongCount(child => child.Left > owner.Left
                && child.Left <= row.Node.Left
                && child.Right >= row.Node.Right
                && child.Right < owner.Right
                && nodes.Any(parent => parent.Id == child.ParentId
                    && (long)child.Depth == (long)parent.Depth + 1
                    && parent.Left >= owner.Left
                    && parent.Right <= owner.Right
                    && parent.Left < child.Left
                    && parent.Right > child.Right))
            == (long)row.Node.Depth - owner.Depth));

        return nearest
            .GroupBy(row => row.Anchor.Id)
            .OrderBy(group => group.Key)
            .Select(group => new CompositionBenchmarkTotal(
                group.Key,
                group.LongCount(),
                group.Sum(row => (long)row.Node.Id)));
    }
}

/// <summary>Retains a bounded scalar aggregate rather than materializing hierarchy entities.</summary>
/// <param name="Anchor">The independently selected ancestor key.</param>
/// <param name="Contributions">The number of eligible payload nodes.</param>
/// <param name="KeySum">The sum of eligible keys, detecting substitution as well as dropped rows.</param>
internal sealed record CompositionBenchmarkTotal(
    int Anchor,
    long Contributions,
    long KeySum
);
