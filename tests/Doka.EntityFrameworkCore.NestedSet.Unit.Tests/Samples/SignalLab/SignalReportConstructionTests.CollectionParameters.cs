using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportConstructionTests
{
    /// <summary>Only SQLite changes its collection policy; translating either report reader remains deferred.</summary>
    /// <param name="engine">The explicit provider dialect, configured without server discovery.</param>
    /// <param name="catalog">Whether the independently authorized catalog is translated instead of its report.</param>
    [Theory]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    public async Task CollectionSqlKeepsTheQualifiedProviderPolicyWithoutIo(string engine, bool catalog)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var request = Request(false);

        // Act
        var sql = catalog
            ? SignalReportBuilder.Catalog(context, request).ToQueryString()
            : SignalReportBuilder.Build(context, request).ToQueryString();

        // Assert
        Assert.Equal(engine == "Sqlite", sql.Contains("json_each(", StringComparison.OrdinalIgnoreCase));
        // WHY: A command-cache improvement does not justify changing server-provider cardinality plans.
        // PostgreSQL retains its native array translation; the other servers retain ordinary parameters.
        Assert.DoesNotContain("JSON_TABLE(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPENJSON(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Changed private selections reuse both SQLite templates and retain independent scalar results.</summary>
    /// <param name="nearest">Whether metadata selection checks the nearest eligible owner before its values.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqliteReportTemplatesRemainCachedAcrossSelectionValuesAndLengths(bool nearest)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        await CreateSqliteReportSchemaAsync(connection);
        var generated = new SqlGeneratorCounter();
        var compilations = new List<string>();
        var probe = new SignalReportProbe();
        var options = new DbContextOptionsBuilder<SignalLabContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets())
            .AddInterceptors(probe)
            .LogTo(compilations.Add, [CoreEventId.QueryCompilationStarting], LogLevel.Debug);

        await using var provider = CreateCommandCacheProvider(options.Options, generated);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var input = data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any);
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
            input with { AnchorIds = input.AnchorIds.ToArray(), ChannelIds = input.ChannelIds.ToArray() },
        };

        var warm = SignalReportValidation.Snapshot(input);
        _ = await SignalReportBuilder.Catalog(context, warm).ToArrayAsync(CancellationToken.None);
        _ = await SignalReportBuilder.Build(context, warm).ToArrayAsync(CancellationToken.None);
        var actual = new List<(ValidatedSignalReport Request, SignalCatalogRow[] Catalog, SignalReportTotal[] Totals)>();
        generated.Reset();
        compilations.Clear();
        probe.Reset();

        // Act
        foreach (var request in requests)
        {
            var snapshot = SignalReportValidation.Snapshot(request);
            var catalog = await SignalReportBuilder.Catalog(context, snapshot).ToArrayAsync(CancellationToken.None);
            var totals = await SignalReportBuilder.Build(context, snapshot).ToArrayAsync(CancellationToken.None);
            actual.Add((snapshot, catalog, totals));
        }

        // Assert
        Assert.Equal(0, generated.Count);
        Assert.Empty(compilations);
        Assert.Equal(requests.Length * 2, probe.Readers.Count);
        Assert.All(probe.Readers, sql => Assert.Contains("json_each(", sql, StringComparison.OrdinalIgnoreCase));
        Assert.All(actual.Zip(requests), pair =>
        {
            SignalReportValidation.RequireCatalog(pair.First.Request, pair.First.Catalog);
            var expected = SignalReportOracle.Calculate(pair.Second).Where(cell => cell.HasData)
                .Select(cell => (cell.AnchorId, cell.ChannelId, cell.Source, cell.Value)).Order().ToArray();

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
    public async Task CommandTemplateObserverDistinguishesTheOrdinaryCollectionControl(bool collection)
    {
        // Arrange
        var generated = new SqlGeneratorCounter();
        var boundary = new CompositionIoBoundary();
        var compilations = new List<string>();
        var options = ContextOptions("Sqlite", boundary)
            .LogTo(compilations.Add, [CoreEventId.QueryCompilationStarting], LogLevel.Debug);

        await using var provider = CreateCommandCacheProvider(options.Options, generated);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
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
            ? () => context.Nodes.Where(node => Enumerable.Contains(selected, node.Id)).Select(node => node.Id)
            : () => context.Nodes.Where(node => node.Id == scalar).Select(node => node.Id);

        _ = query().ToQueryString();
        generated.Reset();
        compilations.Clear();
        var statements = new List<string>();

        // Act
        foreach (var selection in selections)
        {
            selected = selection;
            scalar = selection.FirstOrDefault();
            statements.Add(query().ToQueryString());
        }

        // Assert
        Assert.Equal(collection ? selections.Length : 0, generated.Count);
        Assert.Empty(compilations);
        Assert.Equal(selections.Length, statements.Count);
        Assert.All(statements, sql => Assert.DoesNotContain("json_each(", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>
    /// Caller buffer mutations cannot replace the JSON-bound selections of already constructed readers.
    /// </summary>
    [Fact]
    public async Task SqliteDeferredJsonSelectionsKeepTheirPrivateSnapshot()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        await CreateSqliteReportSchemaAsync(connection);
        var probe = new SignalReportProbe();
        var options = new DbContextOptionsBuilder<SignalLabContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets())
            .AddInterceptors(probe).Options;

        await using var context = new SignalLabContext(options);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        Guid[] anchors = [SignalReportDataset.Id(1)];
        Guid[] signals = [SignalReportDataset.PulseChannel];
        CapturePort[] sources = [CapturePort.Pulse];
        var input = data.Request() with { AnchorIds = anchors, ChannelIds = signals, Sources = sources };
        var expected = SignalReportOracle.Calculate(input);
        var snapshot = SignalReportValidation.Snapshot(input);
        var catalogQuery = SignalReportBuilder.Catalog(context, snapshot);
        var reportQuery = SignalReportBuilder.Build(context, snapshot);
        probe.Reset();

        // Act
        anchors[0] = SignalReportDataset.Id(4);
        signals[0] = SignalReportDataset.SweepChannel;
        sources[0] = CapturePort.Sweep;
        var catalog = await catalogQuery.ToArrayAsync(CancellationToken.None);
        var totals = await reportQuery.ToArrayAsync(CancellationToken.None);

        // Assert
        SignalReportValidation.RequireCatalog(snapshot, catalog);
        var expectedTotals = expected.Where(cell => cell.HasData)
            .Select(cell => (cell.AnchorId, cell.ChannelId, cell.Source, cell.Value)).Order().ToArray();

        Assert.Equal(expectedTotals, ScalarTotals(totals));

        Assert.All(catalog.Where(row => row.Kind == 0), row => Assert.Equal(SignalReportDataset.Id(1), row.Id));
        Assert.All(catalog.Where(row => row.Kind == 1),
            row => Assert.Equal(SignalReportDataset.PulseChannel, row.Id));
        Assert.Equal(2, probe.Readers.Count);
        Assert.All(probe.Readers, sql =>
        {
            Assert.Contains("json_each(", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SignalReportDataset.Id(1).ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SignalReportDataset.PulseChannel.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Contains(probe.ParameterValues, value => value is string text
            && text.Contains(SignalReportDataset.Id(1).ToString(), StringComparison.OrdinalIgnoreCase));

        Assert.Contains(probe.ParameterValues, value => value is string text
            && text.Contains(SignalReportDataset.PulseChannel.ToString(), StringComparison.OrdinalIgnoreCase));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An empty collection remains a request error rather than an unrestricted JSON-backed reader.</summary>
    /// <param name="selection">The required collection removed from an otherwise valid request.</param>
    [Theory]
    [InlineData("anchors")]
    [InlineData("signals")]
    [InlineData("sources")]
    public async Task EmptySelectionsAreRejectedBeforeSqliteIo(string selection)
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions("Sqlite", boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        var data = new SignalReportDataset(Guid.NewGuid(), Guid.NewGuid(), [SignalReportDataset.Id(1)], false);
        var input = data.Request();
        var request = selection switch
        {
            "anchors" => input with { AnchorIds = [] },
            "signals" => input with { ChannelIds = [] },
            "sources" => input with { Sources = [] },
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };

        // Act
        var error = await Record.ExceptionAsync(
            () => SignalReportBuilder.ReadAsync(context, request, CancellationToken.None));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Equal("request", argument.ParamName);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A warmed JSON template never reuses an earlier anchor or metric authorization decision.</summary>
    /// <param name="selection">The private selection replaced with an unauthorized identity.</param>
    /// <param name="foreign">Whether the identity exists outside the bound tree or tenant instead of being absent.</param>
    [Theory]
    [InlineData("anchors", false)]
    [InlineData("anchors", true)]
    [InlineData("signals", false)]
    [InlineData("signals", true)]
    public async Task WarmedSqliteReadRejectsMissingAndForeignSelectionsBeforeTheAggregate(
        string selection,
        bool foreign
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        await CreateSqliteReportSchemaAsync(connection);
        var generated = new SqlGeneratorCounter();
        var compilations = new List<string>();
        var probe = new SignalReportProbe();
        var options = new DbContextOptionsBuilder<SignalLabContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets())
            .AddInterceptors(probe)
            .LogTo(compilations.Add, [CoreEventId.QueryCompilationStarting], LogLevel.Debug);

        await using var provider = CreateCommandCacheProvider(options.Options, generated);
        await using var context = new SignalLabContext(options.UseInternalServiceProvider(provider).Options);
        TestMaterializationObserver.Instance.Register(context, probe.Materialized);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var otherTenant = await context.NodeKinds.Where(type => type.TenantId != data.TenantId)
            .Select(type => type.TenantId).FirstAsync(CancellationToken.None);

        var foreignSignal = SignalReportDataset.Id(998);
        context.SignalChannels.Add(new SignalChannel
        {
            TenantId = otherTenant,
            Id = foreignSignal,
            MeasurementTypeId = SignalReportDataset.SensorKind,
            Name = "Another tenant's metric",
            UnitCode = "count",
        });

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var valid = data.Request() with
        {
            AnchorIds = [SignalReportDataset.Id(1)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
        };

        // WHY: Seed isolation already provides anchor 10 in another tree. The separate tenant's metric
        // distinguishes an unauthorized stored key from a missing key while preserving the warmed shape.
        var rejectedId = (selection, foreign) switch
        {
            ("anchors", true) => SignalReportDataset.Id(10),
            ("signals", true) => foreignSignal,
            _ => SignalReportDataset.Id(999),
        };

        var rejected = selection == "anchors"
            ? valid with { AnchorIds = [rejectedId] }
            : valid with { ChannelIds = [rejectedId] };

        _ = await SignalReportBuilder.ReadAsync(context, valid, CancellationToken.None);
        generated.Reset();
        compilations.Clear();
        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(
            () => SignalReportBuilder.ReadAsync(context, rejected, CancellationToken.None));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Equal("request", argument.ParamName);
        var catalog = Assert.Single(probe.Readers);
        Assert.Contains("json_each(", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SUM(", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GROUP BY", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(probe.ParameterValues, value => value is string text
            && text.Contains(rejectedId.ToString(), StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0, generated.Count);
        Assert.Empty(compilations);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Compares only SQL's scalar group contract, independently of row order and DTO identity.</summary>
    private static (Guid Anchor, Guid Instrument, CapturePort Source, double Value)[] ScalarTotals(
        IEnumerable<SignalReportTotal> totals
    ) => totals.Select(total => (total.AnchorId, total.ChannelId, total.Source, total.Value)).Order().ToArray();

    /// <summary>Creates only the fixture schema before report-specific I/O observation is installed.</summary>
    private static async Task CreateSqliteReportSchemaAsync(SqliteConnection connection)
    {
        // WHY: SQLite's EnsureCreatedAsync performs its HasTables check synchronously. Schema setup belongs
        // to this separate context; the actual report context retains the strict asynchronous command guard.
        var options = new DbContextOptionsBuilder<SignalLabContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets()).Options;
        await using var bootstrap = new SignalLabContext(options);
        await bootstrap.Database.EnsureCreatedAsync(CancellationToken.None);
    }

    /// <summary>Wraps the installed public generator factory while preserving all actual provider services.</summary>
    private static ServiceProvider CreateCommandCacheProvider(DbContextOptions options, SqlGeneratorCounter counter)
    {
        IServiceCollection services = new ServiceCollection();
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        // WHY: The caller owns this internal provider. EF rejects option-level singleton interceptors
        // with UseInternalServiceProvider, so register the shared observer in its actual service graph.
        services.AddSingleton<IInterceptor>(TestMaterializationObserver.Instance);
        var original = Assert.Single(services,
            descriptor => descriptor.ServiceType == typeof(IQuerySqlGeneratorFactory));

        Assert.False(original.IsKeyedService);
        services.Remove(original);
        // WHY: Replacing the provider factory with a generic generator would measure different SQL. Decorate
        // its actual descriptor and lifetime; the public Create call reveals command-template regeneration.
        services.Add(new ServiceDescriptor(typeof(IQuerySqlGeneratorFactory), provider =>
        {
            var instance = original.ImplementationInstance
                ?? original.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType
                    ?? throw new InvalidOperationException("The provider factory has no activation descriptor."));

            return new ObservedSqlGeneratorFactory(Assert.IsAssignableFrom<IQuerySqlGeneratorFactory>(instance),
                counter);
        }, original.Lifetime));

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
    private sealed class ObservedSqlGeneratorFactory(IQuerySqlGeneratorFactory original, SqlGeneratorCounter counter)
        : IQuerySqlGeneratorFactory
    {
        /// <inheritdoc />
        public QuerySqlGenerator Create()
        {
            counter.Created();

            return original.Create();
        }
    }
}
