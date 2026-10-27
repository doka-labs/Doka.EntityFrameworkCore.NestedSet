using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Qualifies the actual signal sample builder with scalar SQL, direct-edge periods and typed metadata.</summary>
[Collection("Model compatibility")]
public abstract partial class SignalReportTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the provider's shared model database with one unique tenant per test.</summary>
    protected SignalReportTests(IProviderFixture<ModelCompatibilityDatabase> fixture) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// The report scenario aggregates four overlapping anchors, two metrics and three physical sources.
    /// </summary>
    [Fact]
    public async Task BundledReportPreservesSourcesAndMeasurementGrain()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = data.Request();
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(24, cells.Length);
        var expected = SignalReportOracle.Calculate(request).ToDictionary(cell => (cell.AnchorId, cell.ChannelId, cell.Source));
        Assert.All(cells, cell => Assert.Equal(expected[(cell.AnchorId, cell.ChannelId, cell.Source)], cell));
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// Each fact's calendar day decides the upper anchor's pruning; the lower self-anchor retains its own facts.
    /// </summary>
    [Theory]
    [InlineData(0, 50, 40)]
    [InlineData(1, 20, 50)]
    public async Task TemporalReportPrunesAtEachMeasurementDay(int day, double rootTotal, double childTotal)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 6, false, CancellationToken.None, Guid.NewGuid());
        var request = data.Request() with
        {
            AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(5)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
            FromInclusive = SignalReportDataset.FirstDay.AddDays(day), UntilExclusive = SignalReportDataset.FirstDay.AddDays(day + 1),
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(rootTotal, cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(1)).Value);
        Assert.Equal(childTotal, cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(5)).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// The stored beginning of an exact edge is inclusive and is checked against each measurement day.
    /// </summary>
    [Theory]
    [InlineData(0, 70)]
    [InlineData(1, 60)]
    [InlineData(2, 40)]
    public async Task TemporalReportHonorsTheInclusiveStartDay(int firstAllowedDay, double expected)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 6, false, CancellationToken.None, Guid.NewGuid());
        var first = SignalReportDataset.FirstDay.AddDays(firstAllowedDay);
        await context.LinkAvailability.Where(period => period.TenantId == data.TenantId
            && period.ParentId == SignalReportDataset.Id(3) && period.ChildId == SignalReportDataset.Id(4))
            .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.FromDate,
                first),
                CancellationToken.None);
        var request = SingleAnchor(data);
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(expected, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>A stored open or maximal end remains valid without incrementing it in SQL or client code.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAndMaximumStoredEndsRemainValid(bool maximumEnd)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 6, false, CancellationToken.None, Guid.NewGuid());
        DateOnly? end = maximumEnd ? DateOnly.MaxValue : null;
        await context.LinkAvailability.Where(period => period.TenantId == data.TenantId
            && period.ParentId == SignalReportDataset.Id(3) && period.ChildId == SignalReportDataset.Id(4))
            .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.ToDate, end), CancellationToken.None);
        var request = SingleAnchor(data);
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(70, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>Overlapping and reopened periods are existential evidence rather than extra fact rows.</summary>
    [Fact]
    public async Task ReopenedAndOverlappingPeriodsDoNotMultiplyMeasurements()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 6, false, CancellationToken.None, Guid.NewGuid());
        context.LinkAvailability.AddRange(
            new LinkWindow
            {
                TenantId = data.TenantId,
                ParentId = SignalReportDataset.Id(3),
                ChildId = SignalReportDataset.Id(4),
                FromDate = SignalReportDataset.FirstDay.AddDays(-17),
                ToDate = DateOnly.MaxValue,
            },
            new LinkWindow
            {
                TenantId = data.TenantId,
                ParentId = SignalReportDataset.Id(3),
                ChildId = SignalReportDataset.Id(5),
                FromDate = SignalReportDataset.FirstDay.AddDays(1),
                ToDate = null,
            });
        await context.SaveChangesAsync(CancellationToken.None);
        var request = SingleAnchor(data);
        context.ChangeTracker.Clear();
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(120, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>A retained ancestor shortcut cannot replace an expired exact direct edge.</summary>
    [Fact]
    public async Task ShortcutCannotReopenAClosedBranch()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 6, false, CancellationToken.None, Guid.NewGuid());
        context.LinkAvailability.Add(new LinkWindow
        {
            TenantId = data.TenantId,
            ParentId = SignalReportDataset.Id(1),
            ChildId = SignalReportDataset.Id(5),
            FromDate = DateOnly.MinValue,
            ToDate = DateOnly.MaxValue,
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var request = SingleAnchor(data) with { FromInclusive = SignalReportDataset.FirstDay.AddDays(1) };
        context.ChangeTracker.Clear();
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(20, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// The nearest scenario selects by classification before checking values, so a failed nearby owner has no fallback.
    /// </summary>
    [Theory]
    [InlineData(false, "Receiver", true)]
    [InlineData(true, "Receiver", false)]
    [InlineData(true, "Relay", true)]
    public async Task NearestReportDoesNotFallbackToAFartherMatchingOwner(bool nearest,
        string moduleClass,
        bool expected)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 8, true, CancellationToken.None, Guid.NewGuid());
        var request = data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any, moduleClass, false) with
        {
            AnchorIds = [SignalReportDataset.Id(1)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.Equal(expected, cell.HasData);
        Assert.Equal(expected ? 10 : 0, cell.Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>Sensor fields on different ancestors cannot combine into a synthetic matching owner.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task MetadataReportRequiresAllFieldsOnTheSameOwner(bool matchingOwner, bool expected)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 8, true, CancellationToken.None, Guid.NewGuid());

        if (matchingOwner)
        {
            await context.TagValues.Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(6) && value.FieldCode == "RackCode")
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value,
                    "R7"),
                    CancellationToken.None);
        }

        var request = SingleAnchor(data) with { Criteria = data.Request(moduleClass: "Relay").Criteria };
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.Equal(expected, cell.HasData);
        Assert.Equal(expected ? 10 : 0, cell.Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// Runs one actual sample context in the provider's existing database without duplicating schema setup.
    /// </summary>
    private async Task<SignalLabContext> CreateContextAsync(SignalReportProbe probe)
    {
        var context = await _fixture.CreateContextAsync<SignalLabContext>(Engine,
            static options => new SignalLabContext(options), probe, TestMaterializationObserver.Instance);

        TestMaterializationObserver.Instance.Register(context, probe.Materialized);

        if (Engine == "SqlServer")
        {
            // WHY: Snapshot is an application database prerequisite, explicitly enabled by this owned test setup only.
            await context.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON",
                CancellationToken.None);
        }

        return context;
    }

    /// <summary>Chooses one anchor, metric and source without changing metadata or temporal semantics.</summary>
    private static SignalReportRequest SingleAnchor(SignalReportDataset data) => data.Request() with
    {
        AnchorIds = [SignalReportDataset.Id(1)],
        ChannelIds = [SignalReportDataset.PulseChannel],
        Sources = [CapturePort.Pulse],
    };

    /// <summary>Checks command and scalar-materialization contracts after a successful actual report.</summary>
    private static void AssertReportBoundary(SignalLabContext context, SignalReportProbe probe)
    {
        Assert.Equal(2, probe.Readers.Count);
        Assert.Contains("SUM", probe.Readers[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", probe.Readers[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
