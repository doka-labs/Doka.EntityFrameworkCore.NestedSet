using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.SignalLab;

/// <summary>Checks actual report expression construction without database I/O.</summary>
public sealed partial class SignalReportConstructionTests
{
    /// <summary>
    /// Builds the fixed four-criterion report without database access on every provider dialect.
    /// </summary>
    /// <param name="engine">The actual provider dialect, configured without server discovery.</param>
    /// <param name="nearest">Whether each metadata group selects the nearest owner rather than any owner.</param>
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
    public async Task Build_FourCriterionReport_RemainsDeferred(
        string engine,
        bool nearest
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

        var request = Request(nearest);

        // Act
        var query = SignalReportBuilder.Build(context, request);

        // Assert
        Assert.NotNull(query.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }

    /// <summary>
    /// Adding one scalar criterion on the same owner preserves deferred report composition.
    /// </summary>
    /// <param name="engine">The actual provider dialect, configured without server discovery.</param>
    /// <param name="nearest">Whether each metadata group selects the nearest owner rather than any owner.</param>
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
    public async Task Build_AdditionalSameOwnerCriterion_RemainsDeferred(
        string engine,
        bool nearest
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

        var baseline = Request(nearest);
        var groups = baseline.Groups.ToArray();
        var sensor = groups[^1];
        groups[^1] = sensor with
        {
            Criteria =
            [
                .. sensor.Criteria,
                new TagCriterion(SignalReportDataset.SensorKind, "CaptureMode", "Continuous", sensor.Mode),
            ],
        };

        var expanded = baseline with { Groups = groups };

        // Act
        var first = SignalReportBuilder.Build(context, baseline);
        var second = SignalReportBuilder.Build(context, expanded);

        // Assert
        Assert.NotNull(first.Expression);
        Assert.NotNull(second.Expression);
        Assert.NotSame(first.Expression, second.Expression);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);

    }

    /// <summary>
    /// Qualifies the complete report's SQL Server translation locally without pretending an ARM runtime executed it.
    /// </summary>
    /// <param name="nearest">Whether each metadata group selects the nearest owner rather than any owner.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Build_SqlServerReport_TranslatesGroupingAndIndependentSources(
        bool nearest
    )
    {
        // Arrange
        var boundary = new CompositionIoBoundary();
        var options = ContextOptions("SqlServer", boundary);
        await using var provider = CreateServiceProvider(options.Options);
        await using var context = new SignalLabContext(
            options
                .UseInternalServiceProvider(provider)
                .Options);

        var request = Request(nearest);

        // Act
        var sql = SignalReportBuilder
            .Build(context, request)
            .ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Requested catalog bindings translate on every dialect without opening a connection.</summary>
    /// <param name="engine">The actual provider configured without server discovery.</param>
    /// <param name="withoutCriteria">Whether no metadata fields or choices are requested.</param>
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
    public async Task Catalog_RequestedBindings_TranslateWithoutIo(
        string engine,
        bool withoutCriteria
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

        if (withoutCriteria)
        {
            request = request with { Groups = [] };
        }

        // Act
        var sql = SignalReportBuilder
            .Catalog(context, request)
            .ToQueryString();

        // Assert
        Assert.Contains("SignalChannels", sql, StringComparison.Ordinal);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Configures the actual sample model and fail-fast I/O observers for an explicit dialect.</summary>
    private static DbContextOptionsBuilder<SignalLabContext> ContextOptions(
        string engine,
        CompositionIoBoundary boundary
    )
        => new DbContextOptionsBuilder<SignalLabContext>(
                ModelCompatibilityDatabase.Options<SignalLabContext>(
                    engine,
                    engine == "Sqlite"
                        ? "Data Source=:memory:"
                        : "Server=localhost;Database=signal_builder;User ID=unused"))
            .AddInterceptors(boundary, boundary.Commands);

    /// <summary>Creates an owned service graph with all configured provider and NestedSet services.</summary>
    private static ServiceProvider CreateServiceProvider(
        DbContextOptions options
    )
    {
        // WHY: Each dialect/model graph belongs to its test, rather than populating EF's process-wide cache.
        // Applying the real options extensions preserves the same provider and NestedSet registration as production.
        var services = new ServiceCollection();
        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Prepares the same fixed four-owner-criterion shape used by the report route.</summary>
    private static ValidatedSignalReport Request(
        bool nearest
    )
    {
        var data = new SignalReportDataset(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [
                SignalReportDataset.Id(1), SignalReportDataset.Id(2), SignalReportDataset.Id(3),
                SignalReportDataset.Id(4),
            ],
            false);

        return SignalReportValidation.Snapshot(
            data.Request(
                nearest
                    ? TagOwnerMode.Nearest
                    : TagOwnerMode.Any));
    }
}
