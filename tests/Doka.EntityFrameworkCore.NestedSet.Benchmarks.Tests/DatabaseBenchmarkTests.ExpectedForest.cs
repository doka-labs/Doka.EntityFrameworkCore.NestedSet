namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class DatabaseBenchmarkTests
{
    /// <summary>Derives adjacency directly from the documented shape instead of production import output.</summary>
    private static Dictionary<int, ExpectedNode> ExpectedForest(
        BenchmarkScenario scenario,
        int keyOffset = 0
    )
    {
        var expected = new Dictionary<int, ExpectedNode>();
        var offset = 0;
        // WHY: Tree ordinals independently define the expected identity and remainder partition.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var tree = 0; tree < scenario.Trees; tree++)
        {
            var size = scenario.Nodes / scenario.Trees
                + (tree < scenario.Nodes % scenario.Trees
                    ? 1
                    : 0);

            var identity = new Guid(tree + 1, 0, 0, new byte[8]);
            // WHY: Local ordinals derive the oracle adjacency without reading production import output.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var local = 0; local < size; local++)
            {
                var key = offset + local + keyOffset + 1;
                int? parent = local == 0
                    ? null
                    : offset
                    + keyOffset
                    + 1
                    + scenario.Shape switch
                    {
                        BenchmarkShape.Wide => 0,
                        BenchmarkShape.Deep => local - 1,
                        BenchmarkShape.Balanced => (local - 1) / 2,
                        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
                    };

                var position = local == 0
                    ? 0
                    : scenario.Shape switch
                    {
                        BenchmarkShape.Wide => local - 1,
                        BenchmarkShape.Deep => 0,
                        BenchmarkShape.Balanced => (local - 1) % 2,
                        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
                    };

                expected.Add(
                    key,
                    new ExpectedNode(key, identity, parent, position, FormattableString.Invariant($"Node-{key:D9}")));
            }

            offset += size;
        }

        return expected;
    }

    /// <summary>Computes dense preorder coordinates through an iterative adjacency walk.</summary>
    private static Dictionary<int, (long Left, long Right, int Depth)> ExpectedCoordinates(
        Dictionary<int, ExpectedNode> expected
    )
    {
        var coordinates = new Dictionary<int, (long Left, long Right, int Depth)>();
        foreach (var root in expected.Values.Where(node => node.ParentId is null))
        {
            long next = 1;
            var pending = new Stack<(int Key, int Depth, bool Closing)>();
            pending.Push((root.Id, 0, false));
            while (pending.TryPop(out var current))
            {
                if (current.Closing)
                {
                    coordinates[current.Key] = (coordinates[current.Key].Left, next++, current.Depth);
                    continue;
                }

                coordinates.Add(current.Key, (next++, 0, current.Depth));
                pending.Push((current.Key, current.Depth, true));
                foreach (var child in expected
                    .Values
                    .Where(node => node.ParentId == current.Key)
                    .OrderByDescending(node => node.Position))
                {
                    pending.Push((child.Id, current.Depth + 1, false));
                }
            }
        }

        return coordinates;
    }

    /// <summary>Compares every stored role rather than accepting structural validity or cardinality alone.</summary>
    private static void AssertExpectedForest(
        Dictionary<int, ExpectedNode> expected,
        List<BenchmarkNode> actual
    )
    {
        Assert.Equal(
            expected.Keys.Order(),
            actual
                .Select(node => node.Id)
                .Order());

        var coordinates = ExpectedCoordinates(expected);
        // WHY: Compare each indexed row with the independent oracle while source membership remains fixed.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var nodeIndex = 0; nodeIndex < actual.Count; nodeIndex++)
        {
            var node = actual[nodeIndex];
            var desired = expected[node.Id];
            Assert.Equal(1, node.Scope);
            Assert.Equal(desired.TreeId, node.TreeId);
            Assert.Equal(desired.ParentId, node.ParentId);
            Assert.Equal(desired.Position, node.Position);
            Assert.Equal(desired.Name, node.Name);
            Assert.Equal(coordinates[node.Id].Left, node.Left);
            Assert.Equal(coordinates[node.Id].Right, node.Right);
            Assert.Equal(coordinates[node.Id].Depth, node.Depth);
        }
    }
}
