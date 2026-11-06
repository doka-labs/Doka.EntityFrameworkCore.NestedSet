namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

/// <summary>Verifies wave-spanning owner fixtures before their native timing and allocation observations.</summary>
[Collection(BenchmarkEnvironmentOwnership.Name)]
public sealed class PreparedOwnerImportBenchmarkTests
{
    /// <summary>Gets equivalent written rows across both operations, boundary sizes, and owner states.</summary>
    public static TheoryData<int, bool, PreparedOwnerMode> ImportCases
    {
        get
        {
            var result = new TheoryData<int, bool, PreparedOwnerMode>();

            foreach (var nodes in new[] { 65, 130 })
            {
                foreach (var forest in new[] { false, true })
                {
                    foreach (var mode in Enum.GetValues<PreparedOwnerMode>())
                    {
                        result.Add(nodes, forest, mode);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Every comparison persists its exact topology and leaves its owner usable by a later save.</summary>
    /// <param name="nodes">The import size crossing one or two wave boundaries.</param>
    /// <param name="forest">Whether two independent plans share the owner collection.</param>
    /// <param name="mode">The pre-import owner tracking and inverse collection state.</param>
    [Theory]
    [MemberData(nameof(ImportCases))]
    public async Task ImportRetainsExactTopologyAndReusableOwner(
        int nodes,
        bool forest,
        PreparedOwnerMode mode
    )
    {
        // Arrange
        var benchmark = Create(nodes, mode);

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);
            Func<Task> import = forest
                ? benchmark.Forest
                : benchmark.Subtree;

            // Act
            await import();

            // Assert
            await benchmark.VerifyAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(benchmark.Context.ChangeTracker.AutoDetectChangesEnabled);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Prepared owner membership cannot turn an unexecuted workload into valid evidence.</summary>
    [Fact]
    public async Task VerificationRejectsUnexecutedImport()
    {
        // Arrange
        var benchmark = Create(65, PreparedOwnerMode.Prepared);

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Act
            var error = await Record.ExceptionAsync(() => benchmark.VerifyAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.IsType<InvalidOperationException>(error);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Rejects missing measured rows independently of the importer being benchmarked.</summary>
    [Fact]
    public async Task VerificationRejectsRemovedImportedRow()
    {
        // Arrange
        var benchmark = Create(65, PreparedOwnerMode.Untracked);

        try
        {
            benchmark.Initialize();
            await benchmark.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);
            await benchmark.Subtree();
            await benchmark
                .Context
                .Nodes
                .Where(node => node.Id == 66)
                .ExecuteDeleteAsync(CancellationToken.None);

            // Act
            var error = await Record.ExceptionAsync(() => benchmark.VerifyAsync(TestContext.Current.CancellationToken));

            // Assert
            Assert.IsType<InvalidOperationException>(error);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Large native sizes initialize the same fixture without creating or loading their dataset yet.</summary>
    /// <param name="nodes">The explicit scaling population selected for native observations.</param>
    [Theory]
    [InlineData(1_000)]
    [InlineData(2_000)]
    [InlineData(4_000)]
    [InlineData(8_000)]
    public async Task ExplicitScalingSizeInitializesTheExistingFixture(
        int nodes
    )
    {
        // Arrange
        var benchmark = Create(nodes, PreparedOwnerMode.Prepared);

        try
        {
            // Act
            var error = Record.Exception(benchmark.Initialize);

            // Assert
            Assert.Null(error);
            Assert.NotNull(benchmark.Context);
        }
        finally
        {
            await benchmark.CleanupAsync();
        }
    }

    /// <summary>Native parameter-aware selectors execute only the requested control/prepared comparisons.</summary>
    /// <param name="forest">
    /// Whether the prepared 8,000-row forest or all four subtree control pairs are selected.
    /// </param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScalingSelectorExcludesUnrequestedOwnerModes(
        bool forest
    )
    {
        // Arrange
        const string variable = "NESTEDSET_BENCHMARK_OPTIONS";
        var previous = Environment.GetEnvironmentVariable(variable);
        var options = new BenchmarkRunOptions
        {
            NodeCounts = forest
                ? [8_000]
                : [1_000, 2_000, 4_000, 8_000],
            Shapes = [BenchmarkShape.Wide],
        };

        string[] filters = forest
            ? ["*PreparedOwnerImportBenchmarks.Forest*OwnerMode: Prepared*"]
            :
            [
                "*PreparedOwnerImportBenchmarks.Subtree*OwnerMode: Untracked*",
                "*PreparedOwnerImportBenchmarks.Subtree*OwnerMode: Prepared*",
            ];

        try
        {
            // WHY: ParamsSource reads the same inherited launcher environment as an actual native BDN child.
            options.ApplyEnvironment();
            var configuration = ManualConfig
                .Create(BenchmarkConfiguration.Create(options))
                .AddFilter(new GlobFilter(filters));

            // Act
            using var selection = BenchmarkSelection.Discover(configuration, diagnostics: true);

            // Assert
            Assert.Equal(
                forest
                    ? 1
                    : 8,
                selection.DatabaseCases.Count);

            Assert.All(
                selection.DatabaseCases,
                value =>
                {
                    Assert.Equal(typeof(PreparedOwnerImportBenchmarks), value.Descriptor.Type);
                    Assert.Equal(
                        forest
                            ? nameof(PreparedOwnerImportBenchmarks.Forest)
                            : nameof(PreparedOwnerImportBenchmarks.Subtree),
                        value.Descriptor.WorkloadMethod.Name);

                    Assert.NotEqual(
                        PreparedOwnerMode.LoadedEmpty,
                        (PreparedOwnerMode)value.Parameters[nameof(PreparedOwnerImportBenchmarks.OwnerMode)]);

                    if (forest)
                    {
                        Assert.Equal(
                            PreparedOwnerMode.Prepared,
                            (PreparedOwnerMode)value.Parameters[nameof(PreparedOwnerImportBenchmarks.OwnerMode)]);
                    }
                });

            Assert.Equal(
                options.NodeCounts,
                selection
                    .DatabaseCases
                    .Select(value => (int)value.Parameters[nameof(PreparedOwnerImportBenchmarks.Nodes)])
                    .Distinct()
                    .Order()
                    .ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>Direct fixture setup retains the same minimum dataset contract as native preflight.</summary>
    /// <param name="nodes">An input below the existing ten-node minimum.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void InvalidInputSizeRejectsBeforeContextAcquisition(
        int nodes
    )
    {
        // Arrange
        var benchmark = Create(nodes, PreparedOwnerMode.Prepared);

        // Act
        var error = Record.Exception(benchmark.Initialize);

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Throws<InvalidOperationException>(() => _ = benchmark.Context);
    }

    /// <summary>Builds the declared native fixture without changing the process-wide benchmark environment.</summary>
    private static PreparedOwnerImportBenchmarks Create(
        int nodes,
        PreparedOwnerMode mode
    ) => new()
    {
        Nodes = nodes,
        Shape = BenchmarkShape.Wide,
        Trees = 1,
        Tracked = 0,
        OwnerMode = mode,
    };
}
