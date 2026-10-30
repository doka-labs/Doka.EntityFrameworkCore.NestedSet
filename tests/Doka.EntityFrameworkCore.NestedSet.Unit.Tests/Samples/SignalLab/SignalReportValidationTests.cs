using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

/// <summary>Guards sample-owned request, calendar and result-grid rules once, independently of SQL providers.</summary>
public sealed partial class SignalReportValidationTests(ITestOutputHelper output)
{
    /// <summary>Inclusive boundaries, open ends and the maximum date require no AddDays conversion.</summary>
    [Theory]
    [InlineData(17, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void PeriodUsesInclusiveDays(int day, bool expected)
    {
        // Arrange
        var from = SignalReportDataset.FirstDay;
        var until = SignalReportDataset.FirstDay.AddDays(1);

        // Act
        var allowed = SignalReportValidation.IsAllowed(from, until, SignalReportDataset.FirstDay.AddDays(day));

        // Assert
        Assert.Equal(expected, allowed);
    }

    /// <summary>Open and maximal period ends remain valid on the largest supported calendar day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumDayDoesNotOverflow(bool openEnd)
    {
        // Arrange
        var from = DateOnly.MinValue;
        DateOnly? until = openEnd ? null : DateOnly.MaxValue;

        // Act
        var allowed = SignalReportValidation.IsAllowed(from, until, DateOnly.MaxValue);

        // Assert
        Assert.True(allowed);
    }

    /// <summary>Invalid inclusive intervals fail before persistence.</summary>
    [Fact]
    public void ReversedPeriodIsRejected()
    {
        // Arrange
        var from = SignalReportDataset.FirstDay.AddDays(1);
        var to = SignalReportDataset.FirstDay;

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequirePeriod(from, to));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>Repeated selections and identical criteria do not change the scalar report grain.</summary>
    [Fact]
    public void SnapshotDeduplicatesSelectionsAndScalarCriteria()
    {
        // Arrange
        var request = Request();
        var criterion = request.Criteria[0];
        request = request with
        {
            AnchorIds = [request.AnchorIds[0], request.AnchorIds[0]],
            ChannelIds = [request.ChannelIds[0], request.ChannelIds[0]],
            Sources = [CapturePort.Pulse, CapturePort.Pulse],
            Criteria = [criterion, criterion],
        };

        // Act
        var snapshot = SignalReportValidation.Snapshot(request);

        // Assert
        Assert.Single(snapshot.AnchorIds);
        Assert.Single(snapshot.ChannelIds);
        Assert.Single(snapshot.Sources);
        Assert.Single(Assert.Single(snapshot.Groups).Criteria);
    }

    /// <summary>Two AND values of one scalar field are a request error rather than an implicit OR.</summary>
    [Fact]
    public void ConflictingScalarValuesAreRejected()
    {
        // Arrange
        var request = Request();
        request = request with { Criteria = [request.Criteria[0], request.Criteria[0] with { Value = "Wide" }] };

        // Act
        var error = Record.Exception(() => SignalReportValidation.Snapshot(request));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Equal("request", argument.ParamName);
    }

    /// <summary>One classification cannot silently evaluate its fields against different owner policies.</summary>
    [Fact]
    public void MixedOwnerModesAreRejected()
    {
        // Arrange
        var request = Request();
        request = request with { Criteria = [request.Criteria[0],
            request.Criteria[0] with { OwnerMode = TagOwnerMode.Nearest }] };

        // Act
        var error = Record.Exception(() => SignalReportValidation.Snapshot(request));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.Equal("request", argument.ParamName);
    }

    /// <summary>Unsupported operators and invalid grains are rejected without constructing SQL.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownOperatorOrGrainIsRejected(bool invalidOperator)
    {
        // Arrange
        var request = Request();
        request = invalidOperator
            ? request with { Criteria = [request.Criteria[0] with { Operator = (TagOperator)7 }] }
            : request with { Granularity = (MeasurementGranularity)7 };

        // Act
        var error = Record.Exception(() => SignalReportValidation.Snapshot(request));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>The same fixed catalog accepts its exact codes and choices.</summary>
    [Fact]
    public void ExactCatalogValuesAreAccepted()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var catalog = Catalog(snapshot);

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        Assert.Null(error);
    }

    /// <summary>
    /// Missing definitions, wrong classifications, unknown choices and case variants are clear errors.
    /// </summary>
    [Theory]
    [InlineData("missing-field")]
    [InlineData("wrong-type")]
    [InlineData("unknown-choice")]
    [InlineData("case")]
    public void InvalidCatalogBindingIsRejected(string shape)
    {
        // Arrange
        var request = Request();
        var catalog = Catalog(SignalReportValidation.Snapshot(request));
        var criterion = request.Criteria[0];
        request = request with
        {
            Criteria = [shape switch
            {
                "missing-field" => criterion with { FieldCode = "Missing" },
                "wrong-type" => criterion with { TypeId = SignalReportDataset.SensorKind },
                "unknown-choice" => criterion with { Value = "XX" },
                _ => criterion with { Value = "narrow" },
            }],
        };

        var snapshot = SignalReportValidation.Snapshot(request);

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>Reports do not silently mix missing or incompatible additive units.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("currency")]
    public void IncompatibleMetricUnitsAreRejected(string unit)
    {
        // Arrange
        var request = Request() with { ChannelIds = [SignalReportDataset.PulseChannel, SignalReportDataset.SweepChannel] };
        var snapshot = SignalReportValidation.Snapshot(request);
        var catalog = Catalog(snapshot).Append(new SignalCatalogRow
        {
            Kind = 1,
            Id = SignalReportDataset.SweepChannel,
            UnitCode = unit,
        }).ToArray();

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>A missing authorized selection cannot produce a partial report.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingAnchorOrMetricIsRejected(int kind)
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var catalog = Catalog(snapshot).Where(row => row.Kind != kind).ToArray();

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        Assert.IsType<ArgumentException>(error);
    }

    /// <summary>An absent SQL group remains distinct from a measured zero in the display grid.</summary>
    [Fact]
    public void GridPreservesMissingVersusMeasuredZero()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with { Sources = [CapturePort.Pulse,
            CapturePort.Sweep] });

        SignalReportTotal[] totals =
        [
            new()
            {
                AnchorId = snapshot.AnchorIds[0],
                ChannelId = snapshot.ChannelIds[0],
                Source = CapturePort.Pulse,
                Value = 0,
            },
        ];

        // Act
        var cells = SignalReportValidation.CreateGrid(snapshot, totals);

        // Assert
        Assert.Collection(cells,
            actual =>
            {
                Assert.True(actual.HasData);
                Assert.Equal(0, actual.Value);
            },
            target =>
            {
                Assert.False(target.HasData);
                Assert.Equal(0, target.Value);
            });
    }

    /// <summary>Creates the smallest valid sample request for pure validation.</summary>
    private static SignalReportRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), [SignalReportDataset.Id(1)],
        [SignalReportDataset.PulseChannel], [CapturePort.Pulse], SignalReportDataset.FirstDay, SignalReportDataset.FirstDay.AddDays(2),
        MeasurementGranularity.Daily, [new TagCriterion(SignalReportDataset.ArrayKind, "Band", "Narrow")]);

    /// <summary>Provides independent scalar catalog evidence, without invoking the production catalog query.</summary>
    private static SignalCatalogRow[] Catalog(ValidatedSignalReport request) =>
    [
        new() { Kind = 0, Id = request.AnchorIds[0] },
        new() { Kind = 1, Id = SignalReportDataset.PulseChannel, UnitCode = "count" },
        new() { Kind = 2, TypeId = SignalReportDataset.ArrayKind, FieldCode = "Band" },
        new() { Kind = 3, TypeId = SignalReportDataset.ArrayKind, FieldCode = "Band", Value = "Narrow" },
    ];
}
