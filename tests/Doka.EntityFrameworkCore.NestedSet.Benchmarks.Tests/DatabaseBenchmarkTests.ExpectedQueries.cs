namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class DatabaseBenchmarkTests
{
    /// <summary>Determines exact query identities in public preorder before a query is performed.</summary>
    private static int[] ExpectedQueryKeys(
        Dictionary<int, ExpectedNode> expected,
        string method,
        BenchmarkScenario scenario
    )
    {
        var firstTreeSize = scenario.Nodes / scenario.Trees
            + (scenario.Nodes % scenario.Trees > 0
                ? 1
                : 0);

        var keys = method switch
        {
            nameof(QueryBenchmarks.CompleteTree) or nameof(QueryBenchmarks.TrackedTree) =>
                Enumerable.Range(1, firstTreeSize),
            nameof(QueryBenchmarks.Descendants) => Enumerable.Range(2, firstTreeSize - 1),
            nameof(QueryBenchmarks.FilteredDescendants) => Enumerable
                .Range(2, firstTreeSize - 1)
                .Where(key => key % 2 == 0),
            nameof(QueryBenchmarks.Children) => expected
                .Values
                .Where(node => node.ParentId == 1)
                .Select(node => node.Id),
            nameof(QueryBenchmarks.Ancestors) => AncestorKeys(expected, firstTreeSize),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        var coordinates = ExpectedCoordinates(expected);

        return keys
            .OrderBy(key => coordinates[key].Left)
            .ToArray();
    }

    /// <summary>Follows independently generated parent keys to determine an ancestor result.</summary>
    private static int[] AncestorKeys(
        Dictionary<int, ExpectedNode> expected,
        int key
    )
    {
        List<int> ancestors = [];
        while (expected[key].ParentId is { } parent)
        {
            ancestors.Add(parent);
            key = parent;
        }

        return ancestors.ToArray();
    }
}
