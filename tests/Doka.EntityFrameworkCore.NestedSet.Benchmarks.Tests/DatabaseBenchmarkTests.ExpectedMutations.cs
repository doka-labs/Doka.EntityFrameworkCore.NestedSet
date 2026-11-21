namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class DatabaseBenchmarkTests
{
    /// <summary>Models each public operation's independent structural effect before execution.</summary>
    private static void ApplyExpectedFeature(
        Dictionary<int, ExpectedNode> expected,
        Type type,
        string method,
        BenchmarkScenario scenario
    )
    {
        var firstTreeSize = scenario.Nodes / scenario.Trees
            + (scenario.Nodes % scenario.Trees > 0
                ? 1
                : 0);

        if (type == typeof(InsertBenchmarks))
        {
            int? parent = method == nameof(InsertBenchmarks.Root)
                ? null
                : 1;

            var identity = parent is null
                ? new Guid(-1, 0, 0, new byte[8])
                : expected[1].TreeId;

            var position = method switch
            {
                nameof(InsertBenchmarks.LastChild) => expected.Values.Count(node => node.ParentId == 1),
                nameof(InsertBenchmarks.After) => 1,
                _ => 0,
            };

            InsertExpected(expected, new ExpectedNode(scenario.Nodes + 1, identity, parent, position, "Inserted"));
        }
        else if (type == typeof(MoveBenchmarks))
        {
            MoveExpected(
                expected,
                method == nameof(MoveBenchmarks.LeafBeforeFirstChild)
                    ? firstTreeSize
                    : 3,
                1,
                0);
        }
        else if (type == typeof(CrossTreeMoveBenchmarks))
        {
            var destination = firstTreeSize + 1;
            MoveExpected(expected, 1, destination, expected.Values.Count(node => node.ParentId == destination));
        }
        else if (type == typeof(DeleteBenchmarks))
        {
            var removed = expected[2];
            if (method == nameof(DeleteBenchmarks.Subtree))
            {
                var branch = DescendantKeys(expected, 2, true);
                ShiftPositions(expected, removed.TreeId, removed.ParentId, removed.Position + 1, -1);
                foreach (var key in branch)
                {
                    expected.Remove(key);
                }
            }
            else
            {
                var promoted = expected
                    .Values
                    .Where(node => node.ParentId == 2)
                    .OrderBy(node => node.Position)
                    .ToArray();

                ShiftPositions(expected, removed.TreeId, removed.ParentId, removed.Position + 1, promoted.Length - 1);
                // WHY: Only indexed promotion order determines each replacement sibling position.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < promoted.Length; index++)
                {
                    var node = promoted[index];
                    expected[node.Id] = node with
                    {
                        ParentId = removed.ParentId,
                        Position = removed.Position + index,
                    };
                }

                expected.Remove(2);
            }
        }
        else if (type == typeof(SubtreeImportBenchmarks))
        {
            var branch = ExpectedForest(
                new BenchmarkScenario(Math.Max(10, scenario.Nodes / 10), scenario.Shape, 1, 0),
                scenario.Nodes);

            var position = expected.Values.Count(node => node.ParentId == 1);
            foreach (var node in branch.Values)
            {
                expected.Add(
                    node.Id,
                    node with
                    {
                        TreeId = expected[1].TreeId,
                        ParentId = node.ParentId ?? 1,
                        Position = node.ParentId is null
                            ? position
                            : node.Position,
                    });
            }
        }
        else if (type == typeof(OrderingBenchmarks)
            || type == typeof(OrderingSaveBenchmarks))
        {
            if (method == nameof(OrderingBenchmarks.SortedInsert))
            {
                InsertExpected(expected, new ExpectedNode(scenario.Nodes + 1, expected[1].TreeId, 1, 0, "000-first"));
            }
            else
            {
                var renamed = scenario.Shape == BenchmarkShape.Deep
                    ? firstTreeSize
                    : 3;

                MoveExpected(expected, renamed, expected[renamed].ParentId!.Value, 0);
                expected[renamed] = expected[renamed] with { Name = "000-first" };
            }
        }
    }

    /// <summary>Inserts into a sibling sequence while retaining every existing node.</summary>
    private static void InsertExpected(
        Dictionary<int, ExpectedNode> expected,
        ExpectedNode node
    )
    {
        ShiftPositions(expected, node.TreeId, node.ParentId, node.Position, 1);
        expected.Add(node.Id, node);
    }

    /// <summary>Moves a complete adjacency branch into the destination sibling sequence.</summary>
    private static void MoveExpected(
        Dictionary<int, ExpectedNode> expected,
        int key,
        int parent,
        long position
    )
    {
        var moved = expected[key];
        var identity = expected[parent].TreeId;
        var branch = DescendantKeys(expected, key, true);
        ShiftPositions(expected, moved.TreeId, moved.ParentId, moved.Position + 1, -1);
        expected.Remove(key);
        ShiftPositions(expected, identity, parent, position, 1);
        expected.Add(
            key,
            moved with
            {
                TreeId = identity,
                ParentId = parent,
                Position = position,
            });

        foreach (var descendant in branch.Where(descendant => descendant != key))
        {
            expected[descendant] = expected[descendant] with { TreeId = identity };
        }
    }

    /// <summary>Adjusts siblings in one independent coordinate space.</summary>
    private static void ShiftPositions(
        Dictionary<int, ExpectedNode> expected,
        Guid identity,
        int? parent,
        long start,
        int delta
    )
    {
        var shifted = expected
            .Values
            .Where(node => node.TreeId == identity && node.ParentId == parent && node.Position >= start)
            .ToArray();

        foreach (var node in shifted)
        {
            expected[node.Id] = node with { Position = node.Position + delta };
        }
    }

    /// <summary>Collects branch keys through adjacency, independently of stored intervals.</summary>
    private static int[] DescendantKeys(
        Dictionary<int, ExpectedNode> expected,
        int root,
        bool includeRoot
    )
    {
        List<int> descendants = [];
        var pending = new Stack<int>();
        pending.Push(root);
        while (pending.TryPop(out var key))
        {
            if (key != root || includeRoot)
            {
                descendants.Add(key);
            }

            foreach (var child in expected.Values.Where(node => node.ParentId == key))
            {
                pending.Push(child.Id);
            }
        }

        return descendants.ToArray();
    }
}
