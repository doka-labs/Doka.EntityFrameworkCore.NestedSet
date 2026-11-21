namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Reads a scalar bag without materializing the hierarchy aliases carried by the source.</summary>
    private static Task<LongPathOccurrence[]> ReadLongRowsAsync(
        IQueryable<LongPathRow> rows,
        CancellationToken cancellationToken
    )
        => rows
            .OrderBy(row => row.Upper.Id)
            .ThenBy(row => row.Day)
            .Select(row => new LongPathOccurrence(row.Upper.Id, row.Node.Id, row.Day))
            .ToArrayAsync(cancellationToken);

    /// <summary>Walks explicit Parent identities rather than counting or excluding coordinate intervals.</summary>
    private static LongPathOccurrence[] ExpectedLongRows(
        CompositionNode[] nodes,
        Guid scope,
        Guid tree,
        int firstUpper,
        int lastUpper,
        int lower,
        string filter
    )
    {
        var identities = nodes.ToDictionary(node => (node.Scope, node.Id));
        var result = new List<LongPathOccurrence>();

        foreach (var upper in Enumerable.Range(firstUpper, lastUpper - firstUpper + 1))
        {
            foreach (var day in LongDays())
            {
                var selectedUpper = filter == "nearest"
                    ? nodes.Single(node => node.Scope == scope && node.TreeId == tree && node.Candidate)
                        .Id
                    : upper;

                var current = identities[(scope, lower)];
                var accepted = current.Visible;
                var remaining = nodes.Length;

                while (accepted
                    && current.Id != selectedUpper
                    && remaining-- > 0)
                {
                    if (current.ParentId is not { } parentId
                        || !identities.TryGetValue((scope, parentId), out var parent)
                        || !parent.Visible
                        || parent.TreeId != tree
                        || current.TreeId != tree
                        || parent.Depth + 1 != current.Depth)
                    {
                        accepted = false;
                        break;
                    }

                    if (filter != "nearest"
                        && (!current.EdgeOpen
                            || (filter == "correlated"
                                && (day < current.FromDate
                                    || (current.ToDate.HasValue && day > current.ToDate.Value)))))
                    {
                        accepted = false;
                        break;
                    }

                    current = parent;
                }

                if (accepted
                    && current.Id == selectedUpper
                    && current.Visible
                    && current.TreeId == tree)
                {
                    result.Add(new LongPathOccurrence(upper, lower, day));
                }
            }
        }

        return result
            .OrderBy(row => row.Origin)
            .ThenBy(row => row.Day)
            .ToArray();
    }
}
