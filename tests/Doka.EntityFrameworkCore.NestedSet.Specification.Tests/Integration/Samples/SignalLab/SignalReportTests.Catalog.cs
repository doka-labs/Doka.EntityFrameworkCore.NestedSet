using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Catalog transport depends on requested exact bindings rather than unused fields or choices.</summary>
    /// <param name="nearest">Whether metadata uses nearest-before-value ownership.</param>
    /// <param name="withoutCriteria">Whether the report requires no metadata catalog rows at all.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CatalogReadsOnlyRequestedBindings(
        bool nearest,
        bool withoutCriteria
    )
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var input = data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any);
        await AddUnrequestedCatalogAsync(context, input);
        var request = SignalReportValidation.Snapshot(withoutCriteria ? input with { Criteria = [] } : input);
        var fields = request.Groups.SelectMany(group => group.Criteria)
            .Select(criterion => (criterion.TypeId, criterion.FieldCode)).ToHashSet();

        var choices = request.Groups.SelectMany(group => group.Criteria)
            .Select(criterion => (criterion.TypeId, criterion.FieldCode, criterion.Value)).ToHashSet();

        probe.Reset();

        // Act
        var catalog = await SignalReportBuilder.Catalog(context, request).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(request.AnchorIds.Length + request.ChannelIds.Length + fields.Count + choices.Count, catalog.Length);
        Assert.Equal(fields.Count, catalog.Count(row => row.Kind == 2));
        Assert.Equal(choices.Count, catalog.Count(row => row.Kind == 3));
        Assert.All(catalog.Where(row => row.Kind == 2),
            row => Assert.Contains((row.TypeId, row.FieldCode), fields));
        Assert.All(catalog.Where(row => row.Kind == 3),
            row => Assert.Contains((row.TypeId, row.FieldCode, row.Value), choices));
        Assert.Single(probe.Readers);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Filtering catalog rows preserves independently expected reports and complete read boundaries.</summary>
    /// <param name="nearest">Whether metadata uses nearest-before-value ownership.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnusedCatalogPopulationDoesNotChangeReport(bool nearest)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = data.Request(nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any);
        await AddUnrequestedCatalogAsync(context, request);
        var expected = SignalReportOracle.Calculate(request).ToDictionary(cell => (cell.AnchorId, cell.ChannelId, cell.Source));
        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.Equal(24, cells.Length);
        Assert.All(cells, cell => Assert.Equal(expected[(cell.AnchorId, cell.ChannelId, cell.Source)], cell));
        AssertReportBoundary(context, probe);
    }

    /// <summary>Values belonging to other requested fields cannot authorize a crossed scalar request.</summary>
    [Fact]
    public async Task CatalogCannotAuthorizeCrossedFieldValues()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var request = data.Request() with
        {
            Criteria =
            [
                new TagCriterion(SignalReportDataset.SensorKind, "Cooling", "R7"),
                new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "Liquid"),
            ],
        };

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(
            () => SignalReportBuilder.ReadAsync(context, request, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Single(probe.Readers);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>SQL collation overmatches cannot authorize differently cased or padded request scalars.</summary>
    /// <param name="shape">Whether the request changes a field or value by case or padding.</param>
    /// <param name="nearest">Whether metadata selects the nearest classification before testing its values.</param>
    [Theory]
    [InlineData("field-case", false)]
    [InlineData("field-case", true)]
    [InlineData("field-padding", false)]
    [InlineData("field-padding", true)]
    [InlineData("value-case", false)]
    [InlineData("value-case", true)]
    [InlineData("value-padding", false)]
    [InlineData("value-padding", true)]
    public async Task CatalogRejectsNonOrdinalRequestAliases(
        string shape,
        bool nearest
    )
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var criterion = new TagCriterion(SignalReportDataset.ModuleKind, "ModuleClass", "Receiver",
            nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any);

        var request = data.Request() with
        {
            Criteria = [shape switch
            {
                "field-case" => criterion with { FieldCode = "moduleClasskind" },
                "field-padding" => criterion with { FieldCode = "ModuleClass " },
                "value-case" => criterion with { Value = "receiver" },
                _ => criterion with { Value = "Receiver " },
            }],
        };

        probe.Reset();

        // Act
        var error = await Record.ExceptionAsync(
            () => SignalReportBuilder.ReadAsync(context, request, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Single(probe.Readers);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Deferred catalog templates retain their own context, tenant and requested scalar bindings.</summary>
    [Fact]
    public async Task InterleavedCatalogsKeepTheirOwnRootsAndBindings()
    {
        // Arrange
        var probe = new SignalReportProbe();
        var secondProbe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        await using var secondContext = await CreateContextAsync(secondProbe);
        var first = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None, Guid.NewGuid());
        var second = await SignalReportDataset.CreateAsync(secondContext, 5, false,
            CancellationToken.None, Guid.NewGuid());

        var firstRequest = SignalReportValidation.Snapshot(first.Request() with
        {
            Criteria = [new TagCriterion(SignalReportDataset.ArrayKind, "Band", "Narrow")],
        });

        var secondRequest = SignalReportValidation.Snapshot(second.Request() with
        {
            Criteria = [new TagCriterion(SignalReportDataset.SensorKind, "RackCode", "R7")],
        });

        probe.Reset();
        secondProbe.Reset();

        // Act
        var firstQuery = SignalReportBuilder.Catalog(context, firstRequest);
        var secondQuery = SignalReportBuilder.Catalog(secondContext, secondRequest);
        var secondRows = await secondQuery.ToArrayAsync(CancellationToken.None);
        var firstRows = await firstQuery.ToArrayAsync(CancellationToken.None);

        // Assert
        SignalReportValidation.RequireCatalog(firstRequest, firstRows);
        SignalReportValidation.RequireCatalog(secondRequest, secondRows);
        Assert.Equal("Band", Assert.Single(firstRows.Where(row => row.Kind == 2)).FieldCode);
        Assert.Equal("Narrow", Assert.Single(firstRows.Where(row => row.Kind == 3)).Value);
        Assert.Equal("RackCode", Assert.Single(secondRows.Where(row => row.Kind == 2)).FieldCode);
        Assert.Equal("R7", Assert.Single(secondRows.Where(row => row.Kind == 3)).Value);
        Assert.Single(probe.Readers);
        Assert.Single(secondProbe.Readers);
        Assert.Equal(0, probe.Entities);
        Assert.Equal(0, secondProbe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(secondContext.ChangeTracker.Entries());
    }

    /// <summary>Seeds unselected bindings and crossed choices without changing measured nodes or facts.</summary>
    private static async Task AddUnrequestedCatalogAsync(
        SignalLabContext context,
        SignalReportRequest request
    )
    {
        var criterion = request.Criteria[0];
        for (var index = 0; index < 1000; index++)
        {
            context.TagChoices.Add(new TagChoice
            {
                TenantId = request.TenantId,
                TypeId = criterion.TypeId,
                FieldCode = criterion.FieldCode,
                Value = FormattableString.Invariant($"Unused-{index:D4}"),
            });
        }

        context.TagDefinitions.Add(new TagDefinition
        {
            TenantId = request.TenantId,
            TypeId = criterion.TypeId,
            FieldCode = "UnrequestedField",
        });
        context.TagChoices.Add(new TagChoice
        {
            TenantId = request.TenantId,
            TypeId = criterion.TypeId,
            FieldCode = "UnrequestedField",
            Value = criterion.Value,
        });

        // WHY: Both values occur in the same requested classification. Separate field/value IN filters
        // would overfetch these crossed pairs even though exact post-read validation still rejects them.
        context.TagChoices.AddRange(
            new TagChoice
            {
                TenantId = request.TenantId,
                TypeId = SignalReportDataset.SensorKind,
                FieldCode = "Cooling",
                Value = "R7",
            },
            new TagChoice
            {
                TenantId = request.TenantId,
                TypeId = SignalReportDataset.SensorKind,
                FieldCode = "RackCode",
                Value = "Liquid",
            });
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }
}
