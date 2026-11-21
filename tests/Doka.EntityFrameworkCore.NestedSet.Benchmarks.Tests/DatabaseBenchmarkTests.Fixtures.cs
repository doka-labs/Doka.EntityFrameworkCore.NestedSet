namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests;

public sealed partial class DatabaseBenchmarkTests
{
    /// <summary>Creates a strongly typed action before the operation is executed.</summary>
    /// <remarks>WHY: Reflection is only test discovery and never enters a measured or acted feature call.</remarks>
    private static Func<Task> BindFeature(
        DatabaseBenchmark benchmark,
        string methodName
    )
    {
        var method = benchmark
                .GetType()
                .GetMethod(methodName)
            ?? throw new InvalidOperationException("The selected benchmark method is missing.");

        return method.CreateDelegate<Func<Task>>(benchmark);
    }

    /// <summary>
    /// Reads registry identities and tombstone state from the provider's actual infrastructure table.
    /// </summary>
    /// <param name="context">The context used for the benchmark data and verification reads.</param>
    /// <param name="cancellationToken">Cancellation for the operation's preparation or verification work.</param>
    private static async Task<string[]> ReadRegistryAsync(
        BenchmarkContext context,
        CancellationToken cancellationToken
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<string> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                string.Join(
                    "|",
                    Enumerable
                        .Range(0, reader.FieldCount)
                        .Select(index => reader
                            .GetValue(index)
                            .ToString())));
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

        /// <inheritdoc />
        public async ValueTask DisposeAsync() => await Benchmark.CleanupAsync();

        /// <summary>Creates an independently owned benchmark fixture.</summary>
        /// <param name="type">The benchmark implementation type.</param>
        /// <param name="scenario">The complete isolated fixture population.</param>
        /// <param name="cancellationToken">The caller's cancellation token for the operation.</param>
        internal static async Task<PreparedBenchmark> CreateAsync(
            Type type,
            BenchmarkScenario scenario,
            CancellationToken cancellationToken
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
                async callbackCancellationToken =>
                {
                    benchmark.Initialize();
                    await benchmark.PrepareAsync(callbackCancellationToken);
                },
                cancellationToken);

            return prepared;
        }
    }
}
