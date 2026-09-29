namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Builds reproducible import plans iteratively, including chains too deep for recursive setup.</summary>
internal sealed class BenchmarkForest
{
    /// <summary>Gets the detached nodes in stable key order.</summary>
    internal required BenchmarkNode[] Nodes { get; init; }

    /// <summary>Gets the public immutable import topology.</summary>
    internal required NestedSetTreeImport<BenchmarkNode, Guid>[] Imports { get; init; }

    /// <summary>Gets the known root keys in independent-tree order.</summary>
    internal required int[] RootKeys { get; init; }

    /// <summary>Gets the direct parent plan independently of the bounds assigned by EF.</summary>
    internal required int?[] Parents { get; init; }

    /// <summary>Creates the complete topology without relying on measured operation output.</summary>
    /// <param name="scenario">The validated node and shape distribution.</param>
    /// <param name="keyOffset">The assigned-key offset for importing a new branch into existing data.</param>
    /// <returns>The forest plan and its independent adjacency expectations.</returns>
    internal static BenchmarkForest Build(
        BenchmarkScenario scenario,
        int keyOffset = 0
    )
    {
        scenario.Validate();
        var nodes = new BenchmarkNode[scenario.Nodes];
        var parents = new int?[scenario.Nodes];
        var branches = new NestedSetBranch<BenchmarkNode>[scenario.Nodes];
        var children = new List<int>?[scenario.Nodes];
        var roots = new int[scenario.Trees];
        var imports = new NestedSetTreeImport<BenchmarkNode, Guid>[scenario.Trees];
        var offset = 0;
        var payload = new string('x', 1024);

        for (var tree = 0; tree < scenario.Trees; tree++)
        {
            var size = (scenario.Nodes / scenario.Trees) + (tree < scenario.Nodes % scenario.Trees ? 1 : 0);
            roots[tree] = offset + keyOffset + 1;

            for (var local = 0; local < size; local++)
            {
                var index = offset + local;
                var key = checked(index + keyOffset + 1);
                nodes[index] = new BenchmarkNode
                {
                    Id = key,
                    Name = FormattableString.Invariant($"Node-{key:D9}"),
                    Payload = payload,
                };

                if (local == 0)
                {
                    continue;
                }

                var parent = offset
                    + (scenario.Shape switch
                    {
                        BenchmarkShape.Wide => 0,
                        BenchmarkShape.Deep => local - 1,
                        BenchmarkShape.Balanced => (local - 1) / 2,
                        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
                    });

                parents[index] = parent + keyOffset + 1;
                (children[parent] ??= []).Add(index);
            }

            // WHY: Reverse construction avoids recursion because every parent precedes its children in key order.
            for (var index = offset + size - 1; index >= offset; index--)
            {
                var descendants = children[index]
                    ?.Select(child => branches[child])
                    .ToArray();

                branches[index] = new NestedSetBranch<BenchmarkNode>(nodes[index], descendants);
            }

            var treeId = new Guid(tree + 1, 0, 0, new byte[8]);
            imports[tree] = new NestedSetTreeImport<BenchmarkNode, Guid>(treeId, branches[offset]);
            offset += size;
        }

        return new BenchmarkForest
        {
            Nodes = nodes,
            Imports = imports,
            RootKeys = roots,
            Parents = parents,
        };
    }
}
