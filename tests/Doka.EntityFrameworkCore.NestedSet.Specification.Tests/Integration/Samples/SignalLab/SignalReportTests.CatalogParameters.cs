using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Caller buffer changes after the catalog cannot alter the later owner predicates or anchors.</summary>
    [Fact]
    public async Task CatalogReadCannotChangeDeferredReportSnapshot()
    {
        // Arrange
        const string missing = "Missing-after-catalog-read";
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var anchors = data.Anchors.ToArray();
        var criteria = data.Request().Criteria.ToArray();
        var request = data.Request() with
        {
            AnchorIds = anchors,
            Criteria = criteria,
        };

        var expected = SignalReportOracle.Calculate(request)
            .ToDictionary(cell => (cell.AnchorId, cell.ChannelId, cell.Source));

        probe.Reset();
        probe.AfterFirstReader = _ =>
        {
            anchors[0] = SignalReportDataset.Id(99);
            criteria[0] = criteria[0] with { Value = missing };

            return Task.CompletedTask;
        };

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(missing, request.Criteria[0].Value);
        Assert.Equal(expected.Count, cells.Length);
        Assert.All(cells, cell => Assert.Equal(expected[(cell.AnchorId, cell.ChannelId, cell.Source)], cell));
        Assert.DoesNotContain(missing, probe.ParameterValues);
        AssertReportBoundary(context, probe);
    }

    /// <summary>
    /// Equal values in different requested fields retain the same parameter slots as distinct values.
    /// </summary>
    [Fact]
    public async Task WarmedCatalogKeepsDistinctSlotsForEqualFieldValues()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        context.TagChoices.Add(new TagChoice
        {
            TenantId = data.TenantId,
            TypeId = SignalReportDataset.SensorKind,
            FieldCode = "RackCode",
            Value = "Liquid",
        });
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var first = SignalReportValidation.Snapshot(data.Request() with
        {
            Criteria =
            [
                new TagCriterion(SignalReportDataset.SensorKind, "Cooling", "Liquid"),
                new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "Liquid"),
            ],
        });

        var second = SignalReportValidation.Snapshot(data.Request() with
        {
            Criteria =
            [
                new TagCriterion(SignalReportDataset.SensorKind, "Cooling", "Liquid"),
                new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "R7"),
            ],
        });

        _ = await SignalReportBuilder.Catalog(context, first).ToArrayAsync(CancellationToken.None);
        probe.Reset();

        // Act
        var secondRows = await SignalReportBuilder.Catalog(context, second).ToArrayAsync(CancellationToken.None);
        var firstRows = await SignalReportBuilder.Catalog(context, first).ToArrayAsync(CancellationToken.None);

        // Assert
        SignalReportValidation.RequireCatalog(first, firstRows);
        SignalReportValidation.RequireCatalog(second, secondRows);
        Assert.Equal("Liquid", firstRows.Single(row => row.Kind == 3 && row.FieldCode == "RackCode").Value);
        Assert.Equal("R7", secondRows.Single(row => row.Kind == 3 && row.FieldCode == "RackCode").Value);
        Assert.Equal(2, probe.Readers.Count);
        Assert.Equal(probe.Readers[0], probe.Readers[1]);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// A warmed catalog shape binds each request's classification, field and scalar text independently.
    /// </summary>
    [Fact]
    public async Task WarmedCatalogBindsEachRequestsTypeFieldAndValue()
    {
        // Arrange
        const string selected = "x' OR 1=1 --";
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var first = SignalReportValidation.Snapshot(data.Request() with
        {
            Criteria = [new TagCriterion(SignalReportDataset.ArrayKind, "Band", "Narrow")],
        });

        var second = SignalReportValidation.Snapshot(data.Request() with
        {
            Criteria = [new TagCriterion(SignalReportDataset.SensorKind, "RackCode", selected)],
        });

        _ = await SignalReportBuilder.Catalog(context, first).ToArrayAsync(CancellationToken.None);
        probe.Reset();

        // Act
        var secondRows = await SignalReportBuilder.Catalog(context, second).ToArrayAsync(CancellationToken.None);
        var firstRows = await SignalReportBuilder.Catalog(context, first).ToArrayAsync(CancellationToken.None);

        // Assert
        SignalReportValidation.RequireCatalog(first, firstRows);
        SignalReportValidation.RequireCatalog(second, secondRows);
        var firstChoice = Assert.Single(firstRows.Where(row => row.Kind == 3));
        var secondChoice = Assert.Single(secondRows.Where(row => row.Kind == 3));
        Assert.Equal((SignalReportDataset.ArrayKind, "Band", "Narrow"),
            (firstChoice.TypeId, firstChoice.FieldCode, firstChoice.Value));
        Assert.Equal((SignalReportDataset.SensorKind, "RackCode", selected),
            (secondChoice.TypeId, secondChoice.FieldCode, secondChoice.Value));
        Assert.Equal(2, probe.Readers.Count);
        Assert.Equal(probe.Readers[0], probe.Readers[1]);
        Assert.Contains(selected, probe.ParameterValues);
        Assert.Contains("Narrow", probe.ParameterValues);
        Assert.All(probe.Readers, sql => Assert.DoesNotContain(selected, sql, StringComparison.Ordinal));
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A missing scalar remains rejected after an equivalent valid catalog shape warmed EF's cache.</summary>
    [Fact]
    public async Task WarmedCatalogDoesNotReusePreviouslyAuthorizedScalar()
    {
        // Arrange
        const string missing = "Missing-after-warm-catalog";
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var valid = data.Request() with
        {
            Criteria = [new TagCriterion(SignalReportDataset.ArrayKind, "Band", "Narrow")],
        };

        _ = await SignalReportBuilder.Catalog(context, SignalReportValidation.Snapshot(valid))
            .ToArrayAsync(CancellationToken.None);
        var invalid = valid with
        {
            Criteria = [valid.Criteria[0] with { Value = missing }],
        };

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(
            () => SignalReportBuilder.ReadAsync(context, invalid, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Single(probe.Readers);
        Assert.Contains(missing, probe.ParameterValues);
        Assert.DoesNotContain(missing, probe.Readers[0], StringComparison.Ordinal);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>
    /// Deferred catalog authorization uses private snapshots even when the caller replaces its inputs.
    /// </summary>
    [Fact]
    public async Task DeferredCatalogKeepsPrivateSnapshotAfterCallerMutation()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        Guid[] anchors = [SignalReportDataset.Id(1)];
        TagCriterion[] criteria = [new(SignalReportDataset.ArrayKind, "Band", "Narrow")];
        var input = data.Request() with
        {
            AnchorIds = anchors,
            Criteria = criteria,
        };

        var snapshot = SignalReportValidation.Snapshot(input);
        var query = SignalReportBuilder.Catalog(context, snapshot);
        probe.Reset();

        // Act
        anchors[0] = SignalReportDataset.Id(99);
        criteria[0] = criteria[0] with { Value = "Missing-after-caller-mutation" };
        var catalog = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        SignalReportValidation.RequireCatalog(snapshot, catalog);
        Assert.Equal(SignalReportDataset.Id(1), Assert.Single(catalog.Where(row => row.Kind == 0)).Id);
        Assert.Equal("Narrow", Assert.Single(catalog.Where(row => row.Kind == 3)).Value);
        Assert.Single(probe.Readers);
        Assert.DoesNotContain("Missing-after-caller-mutation", probe.ParameterValues);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
