namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Creates several ancestor sections with nonmonotonic keys and intervening off-path candidates.</summary>
    private static CompositionNode[] NearestSectionNodes(
        Guid scope,
        Guid treeId,
        string mode
    )
    {
        var nodes = new List<CompositionNode>();
        Add(900, null);
        Add(7, 900);
        Add(0, 7);

        for (var index = 0; index < 50; index++)
        {
            Add(1_000 + index, 0);
        }

        Add(70, 0);
        Add(40, 70);
        Add(200, 70);
        Add(30, 0);
        Add(80, 30);

        while (nodes.Count < 200)
        {
            Add(3_000 + nodes.Count, 200);
        }

        var children = nodes.ToLookup(node => node.ParentId);
        var next = 1L;
        Visit(nodes[0], 0, 0);
        var indexed = nodes.ToDictionary(node => node.Id);
        indexed[0].Matches = true;
        indexed[30].Matches = true;

        switch (mode)
        {
            case "section-owner":
                indexed[70].Candidate = true;
                indexed[70].Matches = true;
                indexed[0].Matches = false;
                break;
            case "zero-owner":
                break;
            case "zero-owner-rejects":
                indexed[0].Matches = false;
                indexed[7].Matches = true;
                indexed[900].Matches = true;
                break;
            case "section-owner-rejects":
                indexed[70].Candidate = true;
                indexed[7].Matches = true;
                indexed[900].Matches = true;
                break;
            case "own-section-owner-rejects":
                indexed[30].Matches = false;
                indexed[7].Matches = true;
                indexed[900].Matches = true;
                break;
            case "none":
                foreach (var node in nodes)
                {
                    node.Candidate = false;
                }

                break;
            case "only-off-path":
                foreach (var key in new[]
                    {
                        900,
                        7,
                        0,
                        70,
                        30,
                    })
                {
                    indexed[key].Candidate = false;
                }

                break;
            case "hidden-above-owner":
                indexed[7].Visible = false;
                break;
            case "hidden-between":
                indexed[70].Visible = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return nodes.ToArray();

        void Add(
            int id,
            int? parent
        ) => nodes.Add(
            new CompositionNode
            {
                Scope = scope,
                TreeId = treeId,
                Id = id,
                ParentId = parent,
                Candidate = id is not (40 or 70 or 80),
                Matches = id >= 1_000 || id == 200,
            });

        void Visit(
            CompositionNode node,
            int depth,
            long position
        )
        {
            node.Left = next++;
            node.Depth = depth;
            node.Position = position;
            var childPosition = 0L;

            foreach (var child in children[node.Id])
            {
                Visit(child, depth + 1, childPosition++);
            }

            node.Right = next++;
        }
    }

    /// <summary>Chooses the first visible Parent-walk candidate before checking its value and visible suffix.</summary>
    private static LongPathOccurrence[] NearestSectionOracle(
        CompositionNode[] nodes
    )
    {
        var indexed = nodes.ToDictionary(node => node.Id);
        var parents = nodes.ToDictionary(node => node.Id, node => node.ParentId);
        var result = new List<LongPathOccurrence>();

        foreach (var lower in new[] { 40, 80 })
        {
            var owner = Ancestors(parents, lower, includeSelf: true)
                .Where(key => indexed[key].Visible && indexed[key].Candidate)
                .Select(key => (int?)key)
                .FirstOrDefault();

            if (owner is not { } selected
                || !indexed[selected].Matches
                || Ancestors(parents, lower, includeSelf: true)
                    .TakeWhile(key => key != selected)
                    .Any(key => !indexed[key].Visible))
            {
                continue;
            }

            foreach (var origin in new[] { 900, 0 })
            {
                foreach (var day in LongDays())
                {
                    result.Add(new LongPathOccurrence(origin, lower, day));
                }
            }
        }

        return result
            .SelectMany(row => new[] { row, row })
            .OrderBy(row => row.Origin)
            .ThenBy(row => row.Node)
            .ThenBy(row => row.Day)
            .ToArray();
    }
}
