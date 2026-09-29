namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies independent topology and stable identity before database measurement begins.</summary>
public sealed class BenchmarkScenarioTests
{
    /// <summary>Every supported shape has deterministic keys, payload, tree identities and adjacency.</summary>
    /// <param name="shape">The generated sibling and ancestor distribution.</param>
    [Theory]
    [InlineData(BenchmarkShape.Wide)]
    [InlineData(BenchmarkShape.Deep)]
    [InlineData(BenchmarkShape.Balanced)]
    public void ForestHasIndependentDeterministicTopology(
        BenchmarkShape shape
    )
    {
        // Arrange
        var scenario = new BenchmarkScenario(31, shape, 3, 4);

        // Act
        var forest = BenchmarkForest.Build(scenario, 100);

        // Assert
        Assert.Equal(Enumerable.Range(101, 31), forest.Nodes.Select(node => node.Id));
        Assert.Equal([101, 112, 122], forest.RootKeys);
        Assert.Equal(3, forest.Imports.Length);
        var actualParents = ReadParents(forest);
        var offset = 0;
        for (var tree = 0; tree < scenario.Trees; tree++)
        {
            var size = (scenario.Nodes / scenario.Trees) + (tree < scenario.Nodes % scenario.Trees ? 1 : 0);
            var import = forest.Imports[tree];
            Assert.Equal(new Guid(tree + 1, 0, 0, new byte[8]), import.TreeId);
            Assert.Equal(101 + offset, import.Root.Entity.Id);

            for (var local = 0; local < size; local++)
            {
                var key = offset + local + 101;
                int? parent = local == 0
                    ? null
                    : offset
                    + 101
                    + (shape switch
                    {
                        BenchmarkShape.Wide => 0,
                        BenchmarkShape.Deep => local - 1,
                        BenchmarkShape.Balanced => (local - 1) / 2,
                        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
                    });

                Assert.Equal(parent, actualParents[key]);
                Assert.Equal(parent, forest.Parents[offset + local]);
                Assert.Equal(FormattableString.Invariant($"Node-{key:D9}"), forest.Nodes[offset + local].Name);
                Assert.Equal(new string('x', 1024), forest.Nodes[offset + local].Payload);
            }

            offset += size;
        }
    }

    /// <summary>Repeated generation retains keys and tree identity without reusing mutable entities.</summary>
    [Fact]
    public void RepeatedForestsHaveStableIdentityAndDistinctEntities()
    {
        // Arrange
        var scenario = new BenchmarkScenario(30, BenchmarkShape.Balanced, 2, 0);
        var previous = BenchmarkForest.Build(scenario);

        // Act
        var current = BenchmarkForest.Build(scenario);

        // Assert
        Assert.Equal(previous.Nodes.Select(node => node.Id), current.Nodes.Select(node => node.Id));
        Assert.Equal(previous.Imports.Select(import => import.TreeId), current.Imports.Select(import => import.TreeId));
        Assert.Equal(previous.Parents, current.Parents);
        Assert.All(current.Nodes.Zip(previous.Nodes), pair => Assert.NotSame(pair.First, pair.Second));
    }

    /// <summary>A one-hundred-thousand-node chain builds and can be traversed without recursive setup.</summary>
    [Fact]
    public void DeepForestSupportsOneHundredThousandNodes()
    {
        // Arrange
        const int nodes = 100000;
        var scenario = new BenchmarkScenario(nodes, BenchmarkShape.Deep, 1, 0);

        // Act
        var forest = BenchmarkForest.Build(scenario);

        // Assert
        var parents = ReadParents(forest);
        Assert.Equal(nodes, parents.Count);
        Assert.Null(parents[1]);
        Assert.Equal(nodes - 1, parents[nodes]);
        Assert.All(Enumerable.Range(2, nodes - 1), key => Assert.Equal(key - 1, parents[key]));
        Assert.Equal(1, Assert.Single(forest.Imports).Root.Entity.Id);
    }

    /// <summary>Incomplete scenarios are rejected before a provider or import plan is acquired.</summary>
    /// <param name="nodes">The total nodes selected for the scenario.</param>
    /// <param name="trees">The independent tree count.</param>
    /// <param name="tracked">The tracker population.</param>
    /// <param name="shape">The selected shape.</param>
    [Theory]
    [InlineData(9, 1, 0, BenchmarkShape.Wide)]
    [InlineData(19, 2, 0, BenchmarkShape.Deep)]
    [InlineData(20, 0, 0, BenchmarkShape.Balanced)]
    [InlineData(20, -1, 0, BenchmarkShape.Balanced)]
    [InlineData(20, 1, -1, BenchmarkShape.Balanced)]
    [InlineData(20, 1, 0, (BenchmarkShape)99)]
    public void InvalidScenarioCannotBuildForest(
        int nodes,
        int trees,
        int tracked,
        BenchmarkShape shape
    )
    {
        // Arrange
        var scenario = new BenchmarkScenario(nodes, shape, trees, tracked);

        // Act
        var error = Record.Exception(() => BenchmarkForest.Build(scenario));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(error);
    }

    /// <summary>Reads the immutable branch plan rather than trusting its auxiliary parent array.</summary>
    /// <remarks>
    /// WHY: Iterative traversal makes the deep-shape regression safe for the test process stack.
    /// </remarks>
    private static Dictionary<int, int?> ReadParents(
        BenchmarkForest forest
    )
    {
        var parents = new Dictionary<int, int?>();
        var pending = new Stack<(NestedSetBranch<BenchmarkNode> Branch, int? Parent)>();
        foreach (var import in forest.Imports)
        {
            pending.Push((import.Root, null));
        }

        while (pending.TryPop(out var current))
        {
            parents.Add(current.Branch.Entity.Id, current.Parent);
            foreach (var child in current.Branch.Children)
            {
                pending.Push((child, current.Branch.Entity.Id));
            }
        }

        return parents;
    }
}
