namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Runs every database benchmark against independently derived SQLite hierarchy expectations.</summary>
public sealed class DatabaseBenchmarkTests
{
    private static readonly Type[] s_databaseTypes =
    [
        typeof(QueryBenchmarks),
        typeof(InsertBenchmarks),
        typeof(MoveBenchmarks),
        typeof(CrossTreeMoveBenchmarks),
        typeof(DeleteBenchmarks),
        typeof(ForestImportBenchmarks),
        typeof(SubtreeImportBenchmarks),
        typeof(OrderingBenchmarks),
        typeof(OrderingSaveBenchmarks),
        typeof(ValidationBenchmarks),
        typeof(RebuildBenchmarks),
    ];

    /// <summary>Gets all measured database methods across each shape and independent-tree configuration.</summary>
    public static TheoryData<Type, string, BenchmarkShape, int> FeatureCases
    {
        get
        {
            var data = new TheoryData<Type, string, BenchmarkShape, int>();
            foreach (var type in s_databaseTypes)
            {
                var methods = type
                    .GetMethods()
                    .Where(method => method.IsDefined(typeof(BenchmarkAttribute)));

                int[] treeCounts = type == typeof(CrossTreeMoveBenchmarks) ? [2] : [1, 2];
                foreach (var method in methods)
                {
                    foreach (var shape in Enum.GetValues<BenchmarkShape>())
                    {
                        foreach (var trees in treeCounts)
                        {
                            data.Add(type, method.Name, shape, trees);
                        }
                    }
                }
            }

            return data;
        }
    }

    /// <summary>Gets each family whose verification must reject an unexecuted feature operation.</summary>
    public static TheoryData<Type> DatabaseFamilies
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var type in s_databaseTypes)
            {
                data.Add(type);
            }

            return data;
        }
    }

    /// <summary>
    /// Each feature persists the exact expected adjacency, membership, ordering and derived coordinates.
    /// </summary>
    /// <param name="type">The concrete feature family.</param>
    /// <param name="methodName">The public measured operation.</param>
    /// <param name="shape">The shape whose subtree work differs.</param>
    /// <param name="trees">The independent coordinate-space count.</param>
    [Theory]
    [MemberData(nameof(FeatureCases))]
    public async Task FeatureHasIndependentExpectedResult(
        Type type,
        string methodName,
        BenchmarkShape shape,
        int trees
    )
    {
        // Arrange
        var scenario = new BenchmarkScenario(32, shape, trees, 3);
        await using var prepared = await PreparedBenchmark.CreateAsync(type, scenario);
        var feature = BindFeature(prepared.Benchmark, methodName);
        var expected = ExpectedForest(scenario);
        var queryKeys = type == typeof(QueryBenchmarks) ? ExpectedQueryKeys(expected, methodName, scenario) : null;
        var hierarchyTrackedBefore = prepared
            .Fixture
            .Context
            .ChangeTracker
            .Entries<BenchmarkNode>()
            .Select(entry => entry.Entity.Id)
            .ToArray();

        ApplyExpectedFeature(expected, type, methodName, scenario);

        // Act
        await feature();

        // Assert
        await prepared.Benchmark.VerifyAsync();
        var nodes = await prepared
            .Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);

        AssertExpectedForest(expected, nodes);
        Assert.Equal(
            3,
            prepared
                .Fixture
                .Context
                .ChangeTracker
                .Entries<BenchmarkTrackedEntity>()
                .Count());

        if (type == typeof(OrderingBenchmarks))
        {
            Assert.Empty(hierarchyTrackedBefore);
        }
        else if (type == typeof(OrderingSaveBenchmarks))
        {
            var renamed = shape == BenchmarkShape.Deep ? scenario.Nodes / trees : 3;
            Assert.Equal([renamed], hierarchyTrackedBefore);
        }

        if (queryKeys is not null)
        {
            var query = (QueryBenchmarks)prepared.Benchmark;
            Assert.Equal(queryKeys, query.Result.Select(node => node.Id));
        }
    }

    /// <summary>An unchanged or deliberately damaged prepared forest never counts as a completed feature.</summary>
    /// <param name="type">The family with independent effect verification.</param>
    [Theory]
    [MemberData(nameof(DatabaseFamilies))]
    public async Task VerificationRejectsUnexecutedFeature(
        Type type
    )
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            type,
            new BenchmarkScenario(32, BenchmarkShape.Balanced, 2, 0));

        // Act
        var error = await Record.ExceptionAsync(prepared.Benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>
    /// Query verification detects missing rows, fabricated identities, wrong trees and invalid preorder.
    /// </summary>
    /// <param name="corruption">The independently injected result corruption.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("tree")]
    [InlineData("preorder")]
    public async Task QueryVerificationRejectsCorruptResult(
        string corruption
    )
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(QueryBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Balanced, 2, 0));

        var benchmark = (QueryBenchmarks)prepared.Benchmark;
        await benchmark.CompleteTree();
        var result = benchmark.Result;
        switch (corruption)
        {
            case "missing":
                result.RemoveAt(0);
                break;
            case "identity":
                result[0] = new BenchmarkNode
                {
                    Id = 999,
                    TreeId = result[0].TreeId
                };
                break;
            case "tree":
                result[0].TreeId = prepared.Fixture.Forest.Imports[1].TreeId;
                break;
            case "preorder":
                result.Reverse();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Correct cardinality cannot hide an inserted node with incorrect sibling placement.</summary>
    [Fact]
    public async Task InsertVerificationRejectsCorruptPlacement()
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(InsertBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Wide, 1, 0));

        var benchmark = (InsertBenchmarks)prepared.Benchmark;
        await benchmark.FirstChild();
        await prepared
            .Fixture
            .Context
            .Nodes
            .Where(node => node.Id == 33)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Position, 1000), CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>A successful validation result cannot hide subsequently corrupted stored coordinates.</summary>
    [Fact]
    public async Task VerificationRejectsCorruptPersistedTree()
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(ValidationBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Deep, 1, 0));

        var benchmark = (ValidationBenchmarks)prepared.Benchmark;
        await benchmark.Full();
        await prepared.Fixture.Context.Nodes.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(node => node.Left, node => node.Left + 100)
                .SetProperty(node => node.Right, node => node.Right + 100),
            CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Reset recreates keys, adjacency, tree identity and tracker state after a fixed-key insertion.</summary>
    [Fact]
    public async Task PreparationRestoresExactBaselineAfterMutation()
    {
        // Arrange
        var scenario = new BenchmarkScenario(32, BenchmarkShape.Balanced, 2, 3);
        await using var prepared = await PreparedBenchmark.CreateAsync(typeof(InsertBenchmarks), scenario);
        var benchmark = (InsertBenchmarks)prepared.Benchmark;
        var registryBefore = await ReadRegistryAsync(prepared.Fixture.Context);
        await benchmark.Root();
        await benchmark.VerifyAsync();

        // Act
        await benchmark.PrepareAsync();

        // Assert
        var actual = await prepared
            .Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);

        AssertExpectedForest(ExpectedForest(scenario), actual);
        Assert.Equal(registryBefore, await ReadRegistryAsync(prepared.Fixture.Context));
        Assert.Equal(
            3,
            prepared
                .Fixture
                .Context
                .ChangeTracker
                .Entries<BenchmarkTrackedEntity>()
                .Count());
        Assert.All(
            prepared.Fixture.Context.ChangeTracker.Entries(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Equal(0, prepared.Fixture.Counter.Commands);
    }

    /// <summary>Deleting the catalog permits the same tree identity and assigned key to be measured again.</summary>
    [Fact]
    public async Task ResetAllowsReusingRootKeyAndTreeIdentity()
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(InsertBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Wide, 2, 0));
        var benchmark = (InsertBenchmarks)prepared.Benchmark;
        await benchmark.Root();
        await benchmark.VerifyAsync();
        await benchmark.PrepareAsync();

        // Act
        await benchmark.Root();

        // Assert
        await benchmark.VerifyAsync();
        var inserted = await prepared
            .Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .SingleAsync(node => node.Id == 33, CancellationToken.None);

        Assert.Equal(new Guid(-1, 0, 0, new byte[8]), inserted.TreeId);
        Assert.Null(inserted.ParentId);
        Assert.Equal(33, await prepared.Fixture.Context.Nodes.CountAsync(CancellationToken.None));
    }

    /// <summary>Creates a strongly typed action before the operation is executed.</summary>
    /// <remarks>WHY: Reflection is only test discovery and never enters a measured or acted feature call.</remarks>
    private static Func<Task> BindFeature(
        DatabaseBenchmark benchmark,
        string methodName
    )
    {
        var method = benchmark.GetType().GetMethod(methodName)
            ?? throw new InvalidOperationException("The selected benchmark method is missing.");

        return method.CreateDelegate<Func<Task>>(benchmark);
    }

    /// <summary>Derives adjacency directly from the documented shape instead of production import output.</summary>
    private static Dictionary<int, ExpectedNode> ExpectedForest(
        BenchmarkScenario scenario,
        int keyOffset = 0
    )
    {
        var expected = new Dictionary<int, ExpectedNode>();
        var offset = 0;
        for (var tree = 0; tree < scenario.Trees; tree++)
        {
            var size = (scenario.Nodes / scenario.Trees) + (tree < scenario.Nodes % scenario.Trees ? 1 : 0);
            var identity = new Guid(tree + 1, 0, 0, new byte[8]);
            for (var local = 0; local < size; local++)
            {
                var key = offset + local + keyOffset + 1;
                int? parent = local == 0
                    ? null
                    : offset
                    + keyOffset
                    + 1
                    + (scenario.Shape switch
                    {
                        BenchmarkShape.Wide => 0,
                        BenchmarkShape.Deep => local - 1,
                        BenchmarkShape.Balanced => (local - 1) / 2,
                        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
                    });

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

    /// <summary>Models each public operation's independent structural effect before execution.</summary>
    private static void ApplyExpectedFeature(
        Dictionary<int, ExpectedNode> expected,
        Type type,
        string method,
        BenchmarkScenario scenario
    )
    {
        var firstTreeSize = (scenario.Nodes / scenario.Trees) + (scenario.Nodes % scenario.Trees > 0 ? 1 : 0);
        if (type == typeof(InsertBenchmarks))
        {
            int? parent = method == nameof(InsertBenchmarks.Root) ? null : 1;
            var identity = parent is null ? new Guid(-1, 0, 0, new byte[8]) : expected[1].TreeId;
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
            MoveExpected(expected, method == nameof(MoveBenchmarks.LeafBeforeFirstChild) ? firstTreeSize : 3, 1, 0);
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
                for (var index = 0; index < promoted.Length; index++)
                {
                    var node = promoted[index];
                    expected[node.Id] = node with
                    {
                        ParentId = removed.ParentId,
                        Position = removed.Position + index
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
                        Position = node.ParentId is null ? position : node.Position,
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
                var renamed = scenario.Shape == BenchmarkShape.Deep ? firstTreeSize : 3;
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
        var descendants = new List<int>();
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

    /// <summary>Determines exact query identities in public preorder before a query is performed.</summary>
    private static int[] ExpectedQueryKeys(
        Dictionary<int, ExpectedNode> expected,
        string method,
        BenchmarkScenario scenario
    )
    {
        var firstTreeSize = (scenario.Nodes / scenario.Trees) + (scenario.Nodes % scenario.Trees > 0 ? 1 : 0);
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
        var ancestors = new List<int>();
        while (expected[key].ParentId is { } parent)
        {
            ancestors.Add(parent);
            key = parent;
        }

        return ancestors.ToArray();
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
        IReadOnlyList<BenchmarkNode> actual
    )
    {
        Assert.Equal(expected.Keys.Order(), actual.Select(node => node.Id).Order());

        var coordinates = ExpectedCoordinates(expected);
        foreach (var node in actual)
        {
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

    /// <summary>
    /// Reads registry identities and tombstone state from the provider's actual infrastructure table.
    /// </summary>
    private static async Task<string[]> ReadRegistryAsync(
        BenchmarkContext context
    )
    {
        var entity = context
            .Model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        var table = entity.GetTableName() ?? throw new InvalidOperationException("The tree registry is not mapped.");
        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY \"TreeId\"";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var rows = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            rows.Add(
                string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index).ToString())));
        }

        return rows.ToArray();
    }

    private sealed record ExpectedNode(
        int Id,
        Guid TreeId,
        int? ParentId,
        long Position,
        string Name
    );

    /// <summary>Owns the same complete lifecycle that BenchmarkDotNet and diagnostics execute.</summary>
    private sealed class PreparedBenchmark : IAsyncDisposable
    {
        private PreparedBenchmark(
            DatabaseBenchmark benchmark
        )
        {
            Benchmark = benchmark;
        }

        internal DatabaseBenchmark Benchmark { get; }

        internal BenchmarkFixture Fixture => Benchmark.Fixture;

        internal static async Task<PreparedBenchmark> CreateAsync(
            Type type,
            BenchmarkScenario scenario
        )
        {
            var benchmark = (DatabaseBenchmark)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("The benchmark requires a public constructor."));

            benchmark.Nodes = scenario.Nodes;
            benchmark.Shape = scenario.Shape;
            benchmark.Trees = scenario.Trees;
            benchmark.Tracked = scenario.Tracked;
            var prepared = new PreparedBenchmark(benchmark);
            await BenchmarkLifecycle.InitializeAsync(
                prepared,
                async () =>
                {
                    benchmark.Initialize();
                    await benchmark.PrepareAsync();
                });

            return prepared;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() => await Benchmark.CleanupAsync();
    }
}
