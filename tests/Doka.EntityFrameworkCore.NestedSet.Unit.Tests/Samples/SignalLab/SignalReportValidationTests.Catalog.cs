using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

public sealed partial class SignalReportValidationTests
{
    /// <summary>
    /// The catalog accepts arbitrary row order and duplicate identical evidence without changing membership.
    /// </summary>
    [Fact]
    public void CatalogAcceptsReorderedDuplicateEvidenceAndNoTagGroups()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request() with { Criteria = [] });
        var catalog = Catalog(snapshot).Reverse().Concat(Catalog(snapshot)).Append(new SignalCatalogRow
        {
            Kind = 99,
            UnitCode = "ignored",
        }).ToArray();

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        Assert.Null(error);
    }

    /// <summary>A valid first selection does not authorize a second missing anchor or metric.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogRequiresEverySelectedIdentity(bool missingMetric)
    {
        // Arrange
        var request = Request();
        var catalog = Catalog(SignalReportValidation.Snapshot(request));
        request = missingMetric
            ? request with { ChannelIds = [SignalReportDataset.PulseChannel, SignalReportDataset.SweepChannel] }
            : request with { AnchorIds = [request.AnchorIds[0], SignalReportDataset.Id(99)] };

        var snapshot = SignalReportValidation.Snapshot(request);

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.StartsWith("A selected anchor or channel is missing", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Missing authorization remains the first reported catalog failure even when the units also disagree.
    /// </summary>
    [Fact]
    public void MissingSelectionPrecedesUnitAndMetadataFailures()
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        SignalCatalogRow[] catalog =
        [
            new()
            {
                Kind = 1,
                Id = SignalReportDataset.PulseChannel,
                UnitCode = "count",
            },
            new()
            {
                Kind = 1,
                Id = SignalReportDataset.SweepChannel,
                UnitCode = "currency",
            },
        ];

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.StartsWith("A selected anchor or channel is missing", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Declared units compare exactly and every returned metric row participates, including duplicate IDs.
    /// </summary>
    [Theory]
    [InlineData("Count", false)]
    [InlineData("count ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("currency", true)]
    public void CatalogRejectsNonidenticalOrMissingUnitEvidence(string? unit, bool duplicateId)
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var catalog = Catalog(snapshot).Append(new SignalCatalogRow
        {
            Kind = 1,
            Id = duplicateId ? SignalReportDataset.PulseChannel : SignalReportDataset.SweepChannel,
            UnitCode = unit!,
        }).ToArray();

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.StartsWith("One additive report requires", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Post-read scalar authorization does not inherit permissive database case or padding comparison.
    /// </summary>
    [Theory]
    [InlineData("definition-type")]
    [InlineData("definition-case")]
    [InlineData("definition-padding")]
    [InlineData("choice-type")]
    [InlineData("choice-field")]
    [InlineData("choice-case")]
    [InlineData("choice-padding")]
    [InlineData("missing-choice")]
    public void CatalogRequiresExactClassificationFieldAndChoiceEvidence(string shape)
    {
        // Arrange
        var snapshot = SignalReportValidation.Snapshot(Request());
        var catalog = Catalog(snapshot);
        var definition = shape.StartsWith("definition", StringComparison.Ordinal);
        catalog[definition ? 2 : 3] = new SignalCatalogRow
        {
            Kind = definition ? 2 : shape == "missing-choice" ? 99 : 3,
            TypeId = shape.EndsWith("type", StringComparison.Ordinal)
                ? SignalReportDataset.SensorKind : SignalReportDataset.ArrayKind,
            FieldCode = shape switch
            {
                "definition-case" => "band",
                "definition-padding" => "Band ",
                "choice-field" => "Carrier",
                _ => "Band",
            },
            Value = shape switch
            {
                "choice-case" => "narrow",
                "choice-padding" => "DE ",
                _ => "Narrow",
            },
        };

        // Act
        var error = Record.Exception(() => SignalReportValidation.RequireCatalog(snapshot, catalog));

        // Assert
        var argument = Assert.IsType<ArgumentException>(error);
        Assert.StartsWith("Unknown field, owner classification", argument.Message, StringComparison.Ordinal);
    }
}
