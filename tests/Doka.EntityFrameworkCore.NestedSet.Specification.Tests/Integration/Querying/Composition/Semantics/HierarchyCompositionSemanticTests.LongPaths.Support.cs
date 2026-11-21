namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Creates canonical chain coordinates while keeping the oracle's Parent links explicit.</summary>
    private static CompositionNode[] LongChain(
        Guid scope,
        Guid tree,
        int first,
        int count
    )
        => Enumerable
            .Range(0, count)
            .Select(depth => new CompositionNode
            {
                Scope = scope,
                TreeId = tree,
                Id = first + depth,
                ParentId = depth == 0
                    ? null
                    : first + depth - 1,
                Left = depth + 1,
                Right = 2L * count - depth,
                Depth = depth,
                Candidate = depth == 0,
                Matches = true,
            })
            .ToArray();

    /// <summary>Joins persisted payload rows to selected visible endpoints before either path strategy.</summary>
    private static IQueryable<LongPathRow> LongRows(
        CompositionContext context,
        Guid scope,
        int firstUpper,
        int lastUpper,
        int lower
    ) => context
        .NestedSet<CompositionNode>()
        .ForScope(scope)
        .SelectSubtrees(
            node => node.Id >= firstUpper && node.Id <= lastUpper,
            (upper, node, distance) => new
            {
                Upper = upper,
                Node = node,
            })
        .Where(row => row.Node.Id == lower)
        .Join(
            context
                .Set<CompositionInputOne>()
                .Where(fact => fact.Scope == scope && fact.NodeId == lower),
            row => row.Node.Id,
            fact => fact.NodeId,
            (pair, fact) => new LongPathRow
            {
                Upper = pair.Upper,
                Node = pair.Node,
                Day = fact.Date,
            });

    /// <summary>Supplies two dates that differ across the explicit per-row edge validity boundary.</summary>
    private static DateOnly[] LongDays() => [new(2044, 10, 18), new(2044, 10, 20)];
}
