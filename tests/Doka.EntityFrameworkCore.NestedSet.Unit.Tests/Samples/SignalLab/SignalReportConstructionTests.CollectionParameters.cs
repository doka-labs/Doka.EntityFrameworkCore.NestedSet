using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
    public async Task Catalog_CollectionSql_KeepsTheQualifiedProviderPolicyWithoutIo(
        string engine,
        bool catalog
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions(engine, boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(
            options
                .UseInternalServiceProvider(provider)
                .Options);

        var request = Request(false);

        // Act
        var sql = catalog
            ? SignalReportBuilder
                .Catalog(context, request)
                .ToQueryString()
            : SignalReportBuilder
                .Build(context, request)
                .ToQueryString();

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

    /// <summary>
    /// Caller buffer mutations cannot replace the JSON-bound selections of already constructed readers.
    /// </summary>
    [Fact]
    public async Task Id_SqliteDeferredJsonSelections_KeepTheirPrivateSnapshot()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        await CreateSqliteReportSchemaAsync(connection, cancellationToken: TestContext.Current.CancellationToken);
        var probe = new SignalReportProbe();
        var options = new DbContextOptionsBuilder<SignalLabContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets())
            .AddInterceptors(probe)
            .Options;

        await using var context = new SignalLabContext(options);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, Guid.NewGuid(), CancellationToken.None);
        Guid[] anchors = [SignalReportDataset.Id(1)];
        Guid[] signals = [SignalReportDataset.PulseChannel];
        CapturePort[] sources = [CapturePort.Pulse];
        var input = data.Request() with
        {
            AnchorIds = anchors,
            ChannelIds = signals,
            Sources = sources,
        };

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
        var expectedTotals = expected
            .Where(cell => cell.HasData)
            .Select(cell => (cell.AnchorId, cell.ChannelId, cell.Source, cell.Value))
            .Order()
            .ToArray();

        Assert.Equal(expectedTotals, ScalarTotals(totals));

        Assert.All(catalog.Where(row => row.Kind == 0), row => Assert.Equal(SignalReportDataset.Id(1), row.Id));
        Assert.All(
            catalog.Where(row => row.Kind == 1),
            row => Assert.Equal(SignalReportDataset.PulseChannel, row.Id));

        Assert.Equal(2, probe.Readers.Count);
        Assert.All(
            probe.Readers,
            sql =>
            {
                Assert.Contains("json_each(", sql, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    SignalReportDataset
                        .Id(1)
                        .ToString(),
                    sql,
                    StringComparison.OrdinalIgnoreCase);

                Assert.DoesNotContain(
                    SignalReportDataset.PulseChannel.ToString(),
                    sql,
                    StringComparison.OrdinalIgnoreCase);
            });

        Assert.Contains(
            probe.ParameterValues,
            value => value is string text
                && text.Contains(
                    SignalReportDataset
                        .Id(1)
                        .ToString(),
                    StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            probe.ParameterValues,
            value => value is string text
                && text.Contains(SignalReportDataset.PulseChannel.ToString(), StringComparison.OrdinalIgnoreCase));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>An empty collection remains a request error rather than an unrestricted JSON-backed reader.</summary>
    /// <param name="selection">The required collection removed from an otherwise valid request.</param>
    [Theory]
    [InlineData("anchors")]
    [InlineData("signals")]
    [InlineData("sources")]
    public async Task ReadAsync_EmptySelections_AreRejectedBeforeSqliteIo(
        string selection
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions("Sqlite", boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(
            options
                .UseInternalServiceProvider(provider)
                .Options);

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
        var error = await Record.ExceptionAsync(() =>
            SignalReportBuilder.ReadAsync(context, request, CancellationToken.None));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Equal("request", argument.ParamName);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>A warmed JSON template never reuses an earlier anchor or metric authorization decision.</summary>
    /// <param name="selection">The private selection replaced with an unauthorized identity.</param>
    /// <param name="foreign">
    /// Whether the identity exists outside the bound tree or tenant instead of being absent.
    /// </param>
    [Theory]
    [InlineData("anchors", false)]
    [InlineData("anchors", true)]
    [InlineData("signals", false)]
    [InlineData("signals", true)]
    public async Task ReadAsync_WarmedSqliteRead_RejectsMissingAndForeignSelectionsBeforeTheAggregate(
        string selection,
        bool foreign
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

        TestMaterializationObserver.Instance.Register(context, probe.Materialized);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, Guid.NewGuid(), CancellationToken.None);
        var otherTenant = await context
            .NodeKinds
            .Where(type => type.TenantId != data.TenantId)
            .Select(type => type.TenantId)
            .FirstAsync(CancellationToken.None);

        var foreignSignal = SignalReportDataset.Id(998);
        context.SignalChannels.Add(
            new SignalChannel
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
        var error = await Record.ExceptionAsync(() =>
            SignalReportBuilder.ReadAsync(context, rejected, CancellationToken.None));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Equal("request", argument.ParamName);
        var catalog = Assert.Single(probe.Readers);
        Assert.Contains("json_each(", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SUM(", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GROUP BY", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            probe.ParameterValues,
            value => value is string text
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
    ) => totals
        .Select(total => (total.AnchorId, total.ChannelId, total.Source, total.Value))
        .Order()
        .ToArray();

    /// <summary>Creates only the fixture schema before report-specific I/O observation is installed.</summary>
    /// <param name="connection">The connection whose ownership transfers to this factory on entry.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static async Task CreateSqliteReportSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        // WHY: SQLite's EnsureCreatedAsync performs its HasTables check synchronously. Schema setup belongs
        // to this separate context; the actual report context retains the strict asynchronous command guard.
        var options = new DbContextOptionsBuilder<SignalLabContext>()
            .UseSqlite(
                connection,
                provider => provider.UseNestedSets())
            .Options;

        await using var bootstrap = new SignalLabContext(options);
        await bootstrap.Database.EnsureCreatedAsync(cancellationToken);
    }
}
