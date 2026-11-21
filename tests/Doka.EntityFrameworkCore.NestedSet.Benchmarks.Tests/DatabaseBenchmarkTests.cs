namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Runs every database benchmark against independently derived SQLite hierarchy expectations.</summary>
public sealed partial class DatabaseBenchmarkTests
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

                int[] treeCounts = type == typeof(CrossTreeMoveBenchmarks)
                    ? [2]
                    : [1, 2];

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
    public async Task BindFeature_SelectedBenchmarkOperation_ReturnsIndependentExpectedResult(
        Type type,
        string methodName,
        BenchmarkShape shape,
        int trees
    )
    {
        // Arrange
        var scenario = new BenchmarkScenario(32, shape, trees, 3);
        await using var prepared = await PreparedBenchmark.CreateAsync(
            type,
            scenario,
            TestContext.Current.CancellationToken);

        var feature = BindFeature(prepared.Benchmark, methodName);
        var expected = ExpectedForest(scenario);
        var queryKeys = type == typeof(QueryBenchmarks)
            ? ExpectedQueryKeys(expected, methodName, scenario)
            : null;

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
        await prepared.Benchmark.VerifyAsync(TestContext.Current.CancellationToken);
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
            var renamed = shape == BenchmarkShape.Deep
                ? scenario.Nodes / trees
                : 3;

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
    public async Task VerifyAsync_Verification_RejectsUnexecutedFeature(
        Type type
    )
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            type,
            new BenchmarkScenario(32, BenchmarkShape.Balanced, 2, 0),
            TestContext.Current.CancellationToken);

        // Act
        var error = await Record.ExceptionAsync(() =>
            prepared.Benchmark.VerifyAsync(TestContext.Current.CancellationToken));

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
    public async Task VerifyAsync_QueryVerification_RejectsCorruptResult(
        string corruption
    )
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(QueryBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Balanced, 2, 0),
            TestContext.Current.CancellationToken);

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
                    TreeId = result[0].TreeId,
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
        var error = await Record.ExceptionAsync(() => benchmark.VerifyAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Correct cardinality cannot hide an inserted node with incorrect sibling placement.</summary>
    [Fact]
    public async Task VerifyAsync_InsertVerification_RejectsCorruptPlacement()
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(InsertBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Wide, 1, 0),
            TestContext.Current.CancellationToken);

        var benchmark = (InsertBenchmarks)prepared.Benchmark;
        await benchmark.FirstChild();
        await prepared
            .Fixture
            .Context
            .Nodes
            .Where(node => node.Id == 33)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Position, 1000), CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => benchmark.VerifyAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>A successful validation result cannot hide subsequently corrupted stored coordinates.</summary>
    [Fact]
    public async Task VerifyAsync_Verification_RejectsCorruptPersistedTree()
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(ValidationBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Deep, 1, 0),
            TestContext.Current.CancellationToken);

        var benchmark = (ValidationBenchmarks)prepared.Benchmark;
        await benchmark.Full();
        await prepared.Fixture.Context.Nodes.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(node => node.Left, node => node.Left + 100)
                .SetProperty(node => node.Right, node => node.Right + 100),
            CancellationToken.None);

        // Act
        var error = await Record.ExceptionAsync(() => benchmark.VerifyAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Reset recreates keys, adjacency, tree identity and tracker state after a fixed-key insertion.</summary>
    [Fact]
    public async Task PrepareAsync_Preparation_RestoresExactBaselineAfterMutation()
    {
        // Arrange
        var scenario = new BenchmarkScenario(32, BenchmarkShape.Balanced, 2, 3);
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(InsertBenchmarks),
            scenario,
            TestContext.Current.CancellationToken);

        var benchmark = (InsertBenchmarks)prepared.Benchmark;
        var registryBefore = await ReadRegistryAsync(prepared.Fixture.Context, TestContext.Current.CancellationToken);
        await benchmark.Root();
        await benchmark.VerifyAsync(TestContext.Current.CancellationToken);

        // Act
        await benchmark.PrepareAsync(TestContext.Current.CancellationToken);

        // Assert
        var actual = await prepared
            .Fixture
            .Context
            .Nodes
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);

        AssertExpectedForest(ExpectedForest(scenario), actual);
        Assert.Equal(
            registryBefore,
            await ReadRegistryAsync(prepared.Fixture.Context, TestContext.Current.CancellationToken));

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
    public async Task Root_Reset_AllowsReusingRootKeyAndTreeIdentity()
    {
        // Arrange
        await using var prepared = await PreparedBenchmark.CreateAsync(
            typeof(InsertBenchmarks),
            new BenchmarkScenario(32, BenchmarkShape.Wide, 2, 0),
            TestContext.Current.CancellationToken);

        var benchmark = (InsertBenchmarks)prepared.Benchmark;
        await benchmark.Root();
        await benchmark.VerifyAsync(TestContext.Current.CancellationToken);
        await benchmark.PrepareAsync(TestContext.Current.CancellationToken);

        // Act
        await benchmark.Root();

        // Assert
        await benchmark.VerifyAsync(TestContext.Current.CancellationToken);
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
}
