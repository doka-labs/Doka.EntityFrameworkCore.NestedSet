using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Retains the real fact-first report's temporal pruning and indexed owner decisions.</summary>
    /// <param name="scenario">The real sample's scenario identity, independent of its structural depth.</param>
    /// <param name="nearest">Whether owner selection rejects values without falling back to a farther owner.</param>
    [Theory]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(64, false)]
    [InlineData(64, true)]
    public async Task ReadAsync_IndexedFactFirstReport_PreservesTemporalAndMetadataContracts(
        int scenario,
        bool nearest
    )
    {
        // Arrange
        var probe = new SignalReportProbe();

        // WHY: Indexed and default reports intentionally use the same physical sample table names.
        // Own this model's database instead of changing the real sample mapping or shared fixture schema.
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await CreateContextAsync<IndexedSignalLabContext>(
            database,
            static options => new IndexedSignalLabContext(options),
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            scenario,
            false,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        var request = data.Request(
            nearest
                ? TagOwnerMode.Nearest
                : TagOwnerMode.Any);

        var expected = SignalReportOracle
            .Calculate(request)
            .ToDictionary(cell => (cell.AnchorId, cell.ChannelId, cell.Source));

        var indexed = Engine is "MySql" or "MariaDb"
            && NestedSetProviderCapabilities.Resolve(context)
                .CompletedSourceMethod is not null
            && NestedSetProviderCapabilities.Resolve(context)
                .OrderedJoinMethod is not null;

        var sql = SignalReportBuilder
            .Build(context, SignalReportValidation.Snapshot(request))
            .ToQueryString();

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(indexed, sql.Contains("WITH RECURSIVE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(indexed, sql.Contains("STRAIGHT_JOIN", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected.Count, cells.Length);
        Assert.All(cells, cell => Assert.Equal(expected[(cell.AnchorId, cell.ChannelId, cell.Source)], cell));
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// Proves that indexed report setup detects entity materialization instead of leaving its guard inert.
    /// </summary>
    [Fact]
    public async Task TenantId_IndexedReportSetup_ObservesMaterialization()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await CreateContextAsync<IndexedSignalLabContext>(
            database,
            static options => new IndexedSignalLabContext(options),
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            5,
            false,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        probe.Reset();

        // Act
        var node = await context
            .Nodes
            .AsNoTracking()
            .FirstAsync(node => node.TenantId == data.TenantId, CancellationToken.None);

        // Assert
        Assert.Equal(data.TenantId, node.TenantId);
        Assert.Equal(1, probe.Entities);
        Assert.Single(probe.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Opts the real sample model into the index without changing its default schema.</summary>
    private sealed class IndexedSignalLabContext
        : SignalLabContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public IndexedSignalLabContext(
            DbContextOptions<IndexedSignalLabContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<SignalNode>()
                .HasNestedSet(mapping => mapping.HasAncestorIndex());
        }
    }
}
