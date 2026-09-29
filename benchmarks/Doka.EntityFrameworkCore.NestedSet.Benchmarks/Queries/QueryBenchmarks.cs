namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Measures composable public queries without separately loading anchor entities.</summary>
public class QueryBenchmarks : DatabaseBenchmark
{
    private List<BenchmarkNode>? _result;
    private int _expected;
    private QueryKind _query;

    /// <summary>Gets the materialized query result for effect verification.</summary>
    internal List<BenchmarkNode> Result =>
        _result ?? throw new InvalidOperationException("Invalid benchmark result: query was not executed.");

    /// <summary>Materializes the complete first tree, including application payload.</summary>
    [Benchmark]
    public async Task CompleteTree()
    {
        _expected = Fixture.LeafKey;
        _query = QueryKind.Tree;
        _result = await Fixture
            .Hierarchy
            .InTree(Fixture.Forest.Imports[0].TreeId)
            .Nodes
            .ToListAsync(CancellationToken.None);
    }

    /// <summary>Materializes descendants using a node-key anchor.</summary>
    [Benchmark]
    public async Task Descendants()
    {
        _expected = Fixture.LeafKey - 1;
        _query = QueryKind.Descendants;
        _result = await Fixture
            .Hierarchy
            .DescendantsOf(1)
            .ToListAsync(CancellationToken.None);
    }

    /// <summary>Filters descendants through ordinary application predicates.</summary>
    [Benchmark]
    public async Task FilteredDescendants()
    {
        _expected = Fixture.LeafKey / 2;
        _query = QueryKind.FilteredDescendants;
        _result = await Fixture
            .Hierarchy
            .DescendantsOf(1)
            .Where(node => node.Id % 2 == 0)
            .ToListAsync(CancellationToken.None);
    }

    /// <summary>Materializes ancestors without including other trees in the same scope.</summary>
    [Benchmark]
    public async Task Ancestors()
    {
        _query = QueryKind.Ancestors;
        _expected = Shape switch
        {
            BenchmarkShape.Wide => 1,
            BenchmarkShape.Deep => Fixture.LeafKey - 1,
            BenchmarkShape.Balanced => (int)Math.Log2(Fixture.LeafKey),
            _ => throw new ArgumentOutOfRangeException(nameof(Shape)),
        };

        _result = await Fixture
            .Hierarchy
            .AncestorsOf(Fixture.LeafKey)
            .ToListAsync(CancellationToken.None);
    }

    /// <summary>Materializes direct children in stable sibling order.</summary>
    [Benchmark]
    public async Task Children()
    {
        _query = QueryKind.Children;
        _expected = Shape switch
        {
            BenchmarkShape.Wide => Fixture.LeafKey - 1,
            BenchmarkShape.Deep => 1,
            BenchmarkShape.Balanced => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(Shape)),
        };

        _result = await Fixture
            .Hierarchy
            .ChildrenOf(1)
            .ToListAsync(CancellationToken.None);
    }

    /// <summary>Materializes a complete tree with EF tracking explicitly enabled.</summary>
    [Benchmark]
    public async Task TrackedTree()
    {
        _expected = Fixture.LeafKey;
        _query = QueryKind.Tree;
        _result = await Fixture
            .Hierarchy
            .InTree(Fixture.Forest.Imports[0].TreeId)
            .Nodes
            .AsTracking()
            .ToListAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public override async Task PrepareAsync()
    {
        _result = null;
        await base.PrepareAsync();
    }

    /// <inheritdoc />
    public override async Task VerifyAsync()
    {
        var result = Result;

        BenchmarkFixture.Require(result.Count == _expected, "Query returned an unexpected count.");
        BenchmarkFixture.Require(
            result.All(node => node.TreeId == Fixture.Forest.Imports[0].TreeId),
            "Query escaped its tree.");

        BenchmarkFixture.Require(
            result
                .Select(node => node.Id)
                .SequenceEqual(ExpectedKeys()),
            "Query returned unexpected keys or hierarchy order.");

        await base.VerifyAsync();
    }

    /// <summary>Derives exact query membership and preorder from the independent, immutable import plan.</summary>
    /// <returns>The independently planned node keys in hierarchy order.</returns>
    private IEnumerable<int> ExpectedKeys()
    {
        var ancestors = new HashSet<int>();

        if (_query == QueryKind.Ancestors)
        {
            var parent = Fixture.Forest.Parents[Fixture.LeafKey - 1];

            while (parent is { } key)
            {
                ancestors.Add(key);
                parent = Fixture.Forest.Parents[key - 1];
            }
        }

        // WHY: An explicit stack also validates deep chains without overflowing the verification call stack.
        var pending = new Stack<NestedSetBranch<BenchmarkNode>>();
        pending.Push(Fixture.Forest.Imports[0].Root);

        while (pending.TryPop(out var branch))
        {
            var key = branch.Entity.Id;
            var include = _query switch
            {
                QueryKind.Tree => true,
                QueryKind.Descendants => key != 1,
                QueryKind.FilteredDescendants => key != 1 && key % 2 == 0,
                QueryKind.Ancestors => ancestors.Contains(key),
                QueryKind.Children => Fixture.Forest.Parents[key - 1] == 1,
                _ => false,
            };

            if (include)
            {
                yield return key;
            }

            for (var index = branch.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(branch.Children[index]);
            }
        }
    }

    /// <summary>Identifies the public query whose independently planned result must be verified.</summary>
    private enum QueryKind
    {
        Tree,
        Descendants,
        FilteredDescendants,
        Ancestors,
        Children,
    }
}
