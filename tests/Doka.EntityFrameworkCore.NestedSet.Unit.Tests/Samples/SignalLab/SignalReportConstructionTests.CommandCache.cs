using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportConstructionTests
{
    /// <summary>Changed private selections reuse both SQLite templates and retain independent scalar results.</summary>
    /// <param name="nearest">Whether metadata selection checks the nearest eligible owner before its values.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snapshot_SqliteReportTemplates_RemainCachedAcrossSelectionValuesAndLengths(
        bool nearest
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        await CreateSqliteReportSchemaAsync(connection, cancellationToken: TestContext.Current.CancellationToken);
        var generated = new SqlGeneratorCounter();
        List<string> compilations = [];
        var probe = new SignalReportProbe();
        var options = new DbContextOptionsBuilder<SignalLabContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets())
            .AddInterceptors(probe)
            .LogTo(compilations.Add, [CoreEventId.QueryCompilationStarting]);

        await using var provider = CreateCommandCacheProvider(options.Options, generated);
        await using var context = new SignalLabContext(
            options
                .UseInternalServiceProvider(provider)
                .Options);

        var data = await SignalReportDataset.CreateAsync(context, 5, false, Guid.NewGuid(), CancellationToken.None);
        var input = data.Request(
            nearest
                ? TagOwnerMode.Nearest
                : TagOwnerMode.Any);

        var requests = new[]
        {
            input with
            {
                AnchorIds = [SignalReportDataset.Id(1)],
                ChannelIds = [SignalReportDataset.PulseChannel],
                Sources = [CapturePort.Pulse],
            },
            input with
            {
                AnchorIds = [SignalReportDataset.Id(4), SignalReportDataset.Id(1)],
                ChannelIds = [SignalReportDataset.SweepChannel, SignalReportDataset.PulseChannel],
                Sources = [CapturePort.Burst, CapturePort.Sweep],
            },
            input with
            {
                AnchorIds = [SignalReportDataset.Id(2), SignalReportDataset.Id(2), SignalReportDataset.Id(3)],
                ChannelIds = [SignalReportDataset.SweepChannel, SignalReportDataset.SweepChannel],
                Sources = [CapturePort.Pulse, CapturePort.Pulse],
            },
            input with
            {
                AnchorIds = input.AnchorIds.ToArray(),
                ChannelIds = input.ChannelIds.ToArray(),
            },
        };

        var warm = SignalReportValidation.Snapshot(input);
        _ = await SignalReportBuilder
            .Catalog(context, warm)
            .ToArrayAsync(CancellationToken.None);

        _ = await SignalReportBuilder
            .Build(context, warm)
            .ToArrayAsync(CancellationToken.None);

        var actual =
            new List<(ValidatedSignalReport Request, SignalCatalogRow[] Catalog, SignalReportTotal[] Totals)>();

        generated.Reset();
        compilations.Clear();
        probe.Reset();

        // Act
        // WHY: These calls must share a warmed context and command cache; isolated theory rows would lose that history.
        // ReSharper disable once ForEachCanBeConvertedToFor
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var request in requests)
        {
            var snapshot = SignalReportValidation.Snapshot(request);
            var catalog = await SignalReportBuilder
                .Catalog(context, snapshot)
                .ToArrayAsync(CancellationToken.None);

            var totals = await SignalReportBuilder
                .Build(context, snapshot)
                .ToArrayAsync(CancellationToken.None);

            actual.Add((snapshot, catalog, totals));
        }

        // Assert
        Assert.Equal(0, generated.Count);
        Assert.Empty(compilations);
        Assert.Equal(requests.Length * 2, probe.Readers.Count);
        Assert.All(probe.Readers, sql => Assert.Contains("json_each(", sql, StringComparison.OrdinalIgnoreCase));
        Assert.All(
            actual.Zip(requests),
            pair =>
            {
                SignalReportValidation.RequireCatalog(pair.First.Request, pair.First.Catalog);
                var expected = SignalReportOracle
                    .Calculate(pair.Second)
                    .Where(cell => cell.HasData)
                    .Select(cell => (cell.AnchorId, cell.ChannelId, cell.Source, cell.Value))
                    .Order()
                    .ToArray();

                Assert.Equal(expected, ScalarTotals(pair.First.Totals));
            });

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// The public observer distinguishes an uncached collection template from a cached scalar control.
    /// </summary>
    /// <param name="collection">Whether ordinary Contains rather than scalar equality is observed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToQueryString_CollectionControl_DistinguishesOrdinaryCommandTemplate(
        bool collection
    )
    {
        // Arrange
        var generated = new SqlGeneratorCounter();
        var boundary = new CompositionIoBoundary();
        List<string> compilations = [];
        var options = ContextOptions("Sqlite", boundary)
            .LogTo(compilations.Add, [CoreEventId.QueryCompilationStarting]);

        await using var provider = CreateCommandCacheProvider(options.Options, generated);
        await using var context = new SignalLabContext(
            options
                .UseInternalServiceProvider(provider)
                .Options);

        Guid[][] selections =
        [
            [SignalReportDataset.Id(1), SignalReportDataset.Id(2)],
            [SignalReportDataset.Id(2)],
            [SignalReportDataset.Id(1), SignalReportDataset.Id(1)],
            [],
        ];

        var selected = selections[0];
        var scalar = selected[0];
        Func<IQueryable<Guid>> query = collection
            ? () => context
                .Nodes
                .Where(node => Enumerable.Contains(selected, node.Id))
                .Select(node => node.Id)
            : () => context
                .Nodes
                .Where(node => node.Id == scalar)
                .Select(node => node.Id);

        _ = query()
            .ToQueryString();

        generated.Reset();
        compilations.Clear();
        List<string> statements = [];

        // Act
        // WHY: Observe changing selection cardinality in one warmed command cache, preserving statement order.
        // ReSharper disable once ForEachCanBeConvertedToFor
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var selection in selections)
        {
            selected = selection;
            scalar = selection.FirstOrDefault();
            statements.Add(
                query()
                    .ToQueryString());
        }

        // Assert
        Assert.Equal(
            collection
                ? selections.Length
                : 0,
            generated.Count);

        Assert.Empty(compilations);
        Assert.Equal(selections.Length, statements.Count);
        Assert.All(statements, sql => Assert.DoesNotContain("json_each(", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>Wraps the installed public generator factory while preserving all actual provider services.</summary>
    private static ServiceProvider CreateCommandCacheProvider(
        DbContextOptions options,
        SqlGeneratorCounter counter
    )
    {
        IServiceCollection services = new ServiceCollection();
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: The caller owns this internal provider. EF rejects option-level singleton interceptors
        // with UseInternalServiceProvider, so register the shared observer in its actual service graph.
        services.AddSingleton<IInterceptor>(TestMaterializationObserver.Instance);
        var original = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IQuerySqlGeneratorFactory));

        Assert.False(original.IsKeyedService);
        services.Remove(original);

        // WHY: Replacing the provider factory with a generic generator would measure different SQL. Decorate
        // its actual descriptor and lifetime; the public Create call reveals command-template regeneration.
        services.Add(
            new ServiceDescriptor(
                typeof(IQuerySqlGeneratorFactory),
                provider =>
                {
                    var instance = original.ImplementationInstance
                        ?? original.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(
                            provider,
                            original.ImplementationType
                            ?? throw new InvalidOperationException(
                                "The provider factory has no activation descriptor."));

                    return new ObservedSqlGeneratorFactory(
                        Assert.IsAssignableFrom<IQuerySqlGeneratorFactory>(instance),
                        counter);
                },
                original.Lifetime));

        return services.BuildServiceProvider();
    }

    /// <summary>Owns one test's thread-safe count without retaining any commands or query bindings.</summary>
    private sealed class SqlGeneratorCounter
    {
        private int _count;

        /// <summary>Gets the number of provider generator creations since the last warmup boundary.</summary>
        internal int Count => Volatile.Read(ref _count);

        /// <summary>Records the public factory call before delegating to the original provider.</summary>
        internal void Created() => Interlocked.Increment(ref _count);

        /// <summary>Removes setup and cold-query generation from the observed warm interval.</summary>
        internal void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    /// <summary>Observes the real provider's generator creation without substituting its SQL implementation.</summary>
    private sealed class ObservedSqlGeneratorFactory
        : IQuerySqlGeneratorFactory
    {
        private readonly IQuerySqlGeneratorFactory _original;
        private readonly SqlGeneratorCounter _counter;

        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ObservedSqlGeneratorFactory(
            IQuerySqlGeneratorFactory original,
            SqlGeneratorCounter counter
        )
        {
            _original = original;
            _counter = counter;
        }

        /// <inheritdoc />
        public QuerySqlGenerator Create()
        {
            _counter.Created();

            return _original.Create();
        }
    }
}
