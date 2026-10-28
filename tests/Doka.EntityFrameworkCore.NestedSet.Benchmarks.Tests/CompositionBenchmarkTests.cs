namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Qualifies equivalent benchmark semantics before drawing observational performance conclusions.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class CompositionBenchmarkTests
{
    /// <summary>Native validation accepts both composition families before database acquisition.</summary>
    /// <param name="type">The pure construction or relational execution family.</param>
    [Theory]
    [InlineData(typeof(CompositionConstructionBenchmarks))]
    [InlineData(typeof(CompositionExecutionBenchmarks))]
    public void NativeValidationAcceptsCompositionFamilies(
        Type type
    )
    {
        // Arrange
        var configuration = BenchmarkConfiguration.Create(new BenchmarkRunOptions());
        using var discovery = BenchmarkConverter.TypeToBenchmarks(type, configuration);

        // Act
        var errors = BenchmarkValidationTestSupport.StructuralErrors(discovery);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Both measured expressions retain independent ancestor contributions to each shape.</summary>
    /// <param name="shape">The independently generated parent distribution.</param>
    /// <param name="reference">Whether the measured expression uses ordinary mapping-specific LINQ.</param>
    [Theory]
    [InlineData(BenchmarkShape.Wide, false)]
    [InlineData(BenchmarkShape.Wide, true)]
    [InlineData(BenchmarkShape.Balanced, false)]
    [InlineData(BenchmarkShape.Balanced, true)]
    [InlineData(BenchmarkShape.Deep, false)]
    [InlineData(BenchmarkShape.Deep, true)]
    public async Task CompositionExecutionHasIndependentAdjacencyResult(
        BenchmarkShape shape,
        bool reference
    )
    {
        // Arrange
        var benchmark = new CompositionExecutionBenchmarks
        {
            Nodes = 32,
            Trees = 2,
            Shape = shape,
            Tracked = 3,
            Anchors = 10,
            PayloadRows = 4,
        };

        await using var cleanup = new CompositionBenchmarkOwner(benchmark);
        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        if (reference)
        {
            await benchmark.Reference();
        }
        else
        {
            await benchmark.Api();
        }

        // Assert
        await benchmark.VerifyAsync();
        Assert.NotEmpty(benchmark.Result);
        Assert.All(benchmark.Result, total => Assert.InRange(total.Anchor, 1, 16));
        Assert.Empty(benchmark.Fixture.Context.ChangeTracker.Entries<BenchmarkNode>());
        Assert.Equal(3, benchmark.Fixture.Context.ChangeTracker.Entries<BenchmarkTrackedEntity>().Count());
    }

    /// <summary>Both complete queries reject a blocked nearest-self payload without falling back to root.</summary>
    /// <param name="reference">Whether the explicit reference or library expression is executed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentReferenceDoesNotReplaceNearestWithAny(
        bool reference
    )
    {
        // Arrange
        await using var fixture = BenchmarkFixture.Create(
            new BenchmarkScenario(20, BenchmarkShape.Deep, 1, 0),
            false,
            new BenchmarkRunOptions { Diagnostics = true },
            "Data Source=:memory:;Pooling=False",
            CancellationToken.None);

        await fixture.ResetAsync(true, CancellationToken.None);
        await fixture
            .Context
            .Nodes
            .Where(node => node.Id == 20)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.Payload, "blocked"),
                CancellationToken.None);

        var tree = fixture.Forest.Imports[0].TreeId;
        var query = reference
            ? CompositionBenchmarkQueries.Reference(fixture.Context, tree, 20, 1, 1)
            : CompositionBenchmarkQueries.Api(fixture.Context, tree, 20, 1, 1);

        fixture.Counter.Reset();

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(rows);
        Assert.Equal(1, fixture.Counter.Commands);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    /// <summary>A failed incoming edge blocks descendants but not the blocked node's own empty self path.</summary>
    /// <param name="reference">Whether the explicit reference or library expression is executed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentReferenceRetainsTheCompletePathContract(
        bool reference
    )
    {
        // Arrange
        await using var fixture = BenchmarkFixture.Create(
            new BenchmarkScenario(20, BenchmarkShape.Deep, 1, 0),
            false,
            new BenchmarkRunOptions { Diagnostics = true },
            "Data Source=:memory:;Pooling=False",
            CancellationToken.None);

        await fixture.ResetAsync(true, CancellationToken.None);
        await fixture
            .Context
            .Nodes
            .Where(node => node.Id == 20)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.Name, "blocked-edge"),
                CancellationToken.None);

        var tree = fixture.Forest.Imports[0].TreeId;
        var query = reference
            ? CompositionBenchmarkQueries.Reference(fixture.Context, tree, 20, 2, 1)
            : CompositionBenchmarkQueries.Api(fixture.Context, tree, 20, 2, 1);

        fixture.Counter.Reset();

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([new CompositionBenchmarkTotal(20, 1, 20)], rows);
        Assert.Equal(1, fixture.Counter.Commands);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    /// <summary>A parent key that skips a stored level cannot masquerade as a complete direct edge.</summary>
    /// <param name="reference">Whether the explicit reference or library expression is executed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentReferenceRejectsParentDepthSkips(
        bool reference
    )
    {
        // Arrange
        await using var fixture = BenchmarkFixture.Create(
            new BenchmarkScenario(20, BenchmarkShape.Deep, 1, 0),
            false,
            new BenchmarkRunOptions { Diagnostics = true },
            "Data Source=:memory:;Pooling=False",
            CancellationToken.None);

        await fixture.ResetAsync(true, CancellationToken.None);
        await fixture
            .Context
            .Nodes
            .Where(node => node.Id == 20)
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.ParentId, 18), CancellationToken.None);

        var tree = fixture.Forest.Imports[0].TreeId;
        var query = reference
            ? CompositionBenchmarkQueries.Reference(fixture.Context, tree, 20, 1, 1)
            : CompositionBenchmarkQueries.Api(fixture.Context, tree, 20, 1, 1);

        fixture.Counter.Reset();

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(rows);
        Assert.Equal(1, fixture.Counter.Commands);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    /// <summary>Verification rejects a family whose selected operation never produced an output.</summary>
    [Fact]
    public async Task CompositionVerificationRejectsUnexecutedOperation()
    {
        // Arrange
        var benchmark = new CompositionExecutionBenchmarks
        {
            Nodes = 20,
            Trees = 1,
            Shape = BenchmarkShape.Wide,
            Anchors = 1,
            PayloadRows = 1,
        };

        await using var cleanup = new CompositionBenchmarkOwner(benchmark);
        benchmark.Initialize();
        await benchmark.PrepareAsync();

        // Act
        var error = await Record.ExceptionAsync(benchmark.VerifyAsync);

        // Assert
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>Construction produces deferred SQL expressions over an unopened in-memory provider.</summary>
    [Fact]
    public async Task ConstructionHasNoPersistedPopulationOrDatabaseAcquisition()
    {
        // Arrange
        var benchmark = new CompositionConstructionBenchmarks
        {
            Anchors = 1_000,
            PayloadRows = 100,
        };

        benchmark.Initialize();

        try
        {
            // Act
            var api = benchmark.Api();
            var reference = benchmark.Reference();

            // Assert
            Assert.IsType<IQueryable>(api, exactMatch: false);
            Assert.IsType<IQueryable>(reference, exactMatch: false);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Owns benchmark cleanup without hiding a failed preparation or verification assertion.</summary>
    private sealed class CompositionBenchmarkOwner : IAsyncDisposable
    {
        private readonly CompositionExecutionBenchmarks _benchmark;

        /// <summary>Registers cleanup before initialization can acquire database resources.</summary>
        internal CompositionBenchmarkOwner(
            CompositionExecutionBenchmarks benchmark
        )
        {
            _benchmark = benchmark;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() => await _benchmark.CleanupAsync();
    }
}
