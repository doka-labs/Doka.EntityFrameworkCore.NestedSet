using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>
    /// Actual relational foreign keys reject undefined fields and values on a node of another classification.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedMetadataForeignKeysRejectInvalidWrites(bool wrongOwnerType)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        var before = await context.TagValues.CountAsync(value => value.TenantId == data.TenantId,
            CancellationToken.None);

        context.TagValues.Add(new TagValue
        {
            TenantId = data.TenantId,
            NodeId = SignalReportDataset.Id(4),
            TypeId = wrongOwnerType ? SignalReportDataset.ArrayKind : SignalReportDataset.SensorKind,
            FieldCode = wrongOwnerType ? "Band" : "Unknown",
            Value = "Narrow",
        });

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var after = await context.TagValues.AsNoTracking().CountAsync(value => value.TenantId == data.TenantId,
            CancellationToken.None);

        Assert.IsAssignableFrom<DbUpdateException>(error);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// The persisted inclusive interval constraint rejects a reversed period independently from the sample validator.
    /// </summary>
    [Fact]
    public async Task DatabaseRejectsAReversedPeriod()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 6, false, CancellationToken.None, Guid.NewGuid());
        context.LinkAvailability.Add(new LinkWindow
        {
            TenantId = data.TenantId,
            ParentId = SignalReportDataset.Id(3),
            ChildId = SignalReportDataset.Id(4),
            FromDate = SignalReportDataset.FirstDay.AddDays(2), ToDate = SignalReportDataset.FirstDay.AddDays(1),
        });

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var count = await context.LinkAvailability.AsNoTracking().CountAsync(period => period.TenantId == data.TenantId,
            CancellationToken.None);

        Assert.IsAssignableFrom<DbUpdateException>(error);
        Assert.Equal(8, count);
    }

    /// <summary>Malformed requests never fall through to a partial SQL aggregate.</summary>
    [Theory]
    [InlineData("field", 1)]
    [InlineData("type", 1)]
    [InlineData("choice", 1)]
    [InlineData("operator", 0)]
    [InlineData("contradiction", 0)]
    public async Task InvalidRequestDoesNotExecuteAnAggregate(string shape, int expectedCatalogReaders)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        var request = SingleAnchor(data);
        var criterion = new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "R7");
        request = request with
        {
            Criteria = shape switch
            {
                "field" => [criterion with { FieldCode = "x' OR 1=1 --" }],
                "type" => [criterion with { TypeId = SignalReportDataset.StationKind }],
                "choice" => [criterion with { Value = "unknown" }],
                "operator" => [criterion with { Operator = (TagOperator)99 }],
                _ => [criterion, criterion with { Value = "R9" }],
            },
        };

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportBuilder.ReadAsync(context,
            request,
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(expectedCatalogReaders, probe.Readers.Count);
        Assert.DoesNotContain(probe.Readers, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>
    /// Visible anchors and metrics must all belong to the authorized selection; no valid subset is silently reported.
    /// </summary>
    [Theory]
    [InlineData("foreign-tree")]
    [InlineData("hidden")]
    [InlineData("missing-metric")]
    public async Task CatalogRejectsAnInvalidSelectionBeforeAnyAggregate(string shape)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        var request = SingleAnchor(data);

        if (shape == "hidden")
        {
            await context.Nodes.Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(4))
                .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.IsVisible,
                    false),
                    CancellationToken.None);
        }

        request = shape switch
        {
            "foreign-tree" => request with { AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(10)] },
            "hidden" => request with { AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(4)] },
            _ => request with { ChannelIds = [SignalReportDataset.PulseChannel, SignalReportDataset.Id(999)] },
        };

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(() => SignalReportBuilder.ReadAsync(context,
            request,
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Single(probe.Readers);
        Assert.DoesNotContain("GROUP BY", probe.Readers[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>Known case-variant choices still compare case-sensitively in actual SQL on every provider.</summary>
    [Theory]
    [InlineData("Receiver", true)]
    [InlineData("receiver", false)]
    public async Task FixedMetadataComparisonMatchesOrdinalRequestPolicy(string selected, bool expected)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        context.TagChoices.Add(new TagChoice
        {
            TenantId = data.TenantId,
            TypeId = SignalReportDataset.ModuleKind,
            FieldCode = "ModuleClass",
            Value = "receiver",
        });
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = SingleAnchor(data) with
        {
            Criteria = [new TagCriterion(SignalReportDataset.ModuleKind, "ModuleClass", selected)],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.Equal(expected, cell.HasData);
        Assert.Equal(expected ? 70 : 0, cell.Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>A separately permitted SQL-like value remains a scalar parameter, never SQL structure.</summary>
    [Fact]
    public async Task SqlLikeMetadataTextIsParameterizedWithoutExpandingTheResult()
    {
        // Arrange
        const string selected = "x' OR 1=1 --";
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        await context.TagValues.Where(value => value.TenantId == data.TenantId
            && value.NodeId == SignalReportDataset.Id(4) && value.FieldCode == "RackCode")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, selected), CancellationToken.None);
        var request = SingleAnchor(data) with
        {
            Criteria = [new TagCriterion(SignalReportDataset.SensorKind, "RackCode", selected)],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(30, Assert.Single(cells).Value);
        Assert.Contains(selected, probe.ParameterValues);
        Assert.DoesNotContain(selected, probe.Readers[1], StringComparison.Ordinal);
        AssertReportBoundary(context, probe);
    }

    /// <summary>Duplicate request IDs and repeated identical fields do not duplicate contributions.</summary>
    [Fact]
    public async Task DuplicateCriteriaAndSelectionsPreserveTheSameSum()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        var request = SingleAnchor(data);
        request = request with
        {
            AnchorIds = [request.AnchorIds[0], request.AnchorIds[0]], ChannelIds = [request.ChannelIds[0], request.ChannelIds[0]],
            Sources = [CapturePort.Pulse, CapturePort.Pulse], Criteria = [.. request.Criteria, .. request.Criteria],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(70, Assert.Single(cells).Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>A valid nearest-owner match never reopens an expired anchor-to-measurement edge.</summary>
    [Fact]
    public async Task NearestMetadataDoesNotReopenATemporallyClosedBranch()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 8, true, CancellationToken.None, Guid.NewGuid());
        await context.LinkAvailability.Where(period => period.TenantId == data.TenantId
            && period.ParentId == SignalReportDataset.Id(4) && period.ChildId == SignalReportDataset.Id(5))
            .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.ToDate,
                SignalReportDataset.FirstDay.AddDays(-1)),
                CancellationToken.None);
        var request = data.Request(TagOwnerMode.Nearest, "Relay", false) with
        {
            AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(6)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.False(cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(1)).HasData);
        Assert.Equal(10, cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(6)).Value);
        AssertReportBoundary(context, probe);
    }
    /// <summary>
    /// Any metadata cannot reopen a hidden anchor path; Nearest additionally requires its own complete owner path.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task VisibilityGapsRemainDistinctForAnyAndNearest(bool nearest, bool selfHasData)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 8, true, CancellationToken.None, Guid.NewGuid());
        await context.Nodes.Where(node => node.TenantId == data.TenantId && node.Id == SignalReportDataset.Id(4))
            .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.IsVisible, false), CancellationToken.None);
        var request = data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any, "Receiver", false) with
        {
            AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(6)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.False(cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(1)).HasData);
        var self = cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(6));
        Assert.Equal(selfHasData, self.HasData);
        Assert.Equal(selfHasData ? 10 : 0, self.Value);
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// A classification with no owner yields no qualifying group, rather than choosing a root default.
    /// </summary>
    [Fact]
    public async Task MissingNearestOwnerDoesNotSupplyARootDefault()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 8, true, CancellationToken.None, Guid.NewGuid());
        var request = data.Request(TagOwnerMode.Nearest, "Receiver", false) with
        {
            TreeId = SignalReportDataset.Id(4008),
            AnchorIds = [SignalReportDataset.Id(10)],
            ChannelIds = [SignalReportDataset.PulseChannel],
            Sources = [CapturePort.Pulse],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.False(cell.HasData);
        Assert.Equal(0, cell.Value);
        AssertReportBoundary(context, probe);
    }

}
