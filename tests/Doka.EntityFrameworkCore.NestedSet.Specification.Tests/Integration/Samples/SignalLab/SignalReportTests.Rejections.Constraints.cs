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
    public async Task SaveChangesAsync_TypedMetadataForeignKeys_RejectInvalidWrites(
        bool wrongOwnerType
    )
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            7,
            false,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        var before = await context.TagValues.CountAsync(
            value => value.TenantId == data.TenantId,
            CancellationToken.None);

        context.TagValues.Add(
            new TagValue
            {
                TenantId = data.TenantId,
                NodeId = SignalReportDataset.Id(4),
                TypeId = wrongOwnerType
                    ? SignalReportDataset.ArrayKind
                    : SignalReportDataset.SensorKind,
                FieldCode = wrongOwnerType
                    ? "Band"
                    : "Unknown",
                Value = "Narrow",
            });

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var after = await context
            .TagValues
            .AsNoTracking()
            .CountAsync(
                value => value.TenantId == data.TenantId,
                CancellationToken.None);

        Assert.IsAssignableFrom<DbUpdateException>(error);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// The persisted inclusive interval constraint rejects a reversed period independently from the sample validator.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_Database_RejectsAReversedPeriod()
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var data = await SignalReportDataset.CreateAsync(
            context,
            6,
            false,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
        context.LinkAvailability.Add(
            new LinkWindow
            {
                TenantId = data.TenantId,
                ParentId = SignalReportDataset.Id(3),
                ChildId = SignalReportDataset.Id(4),
                FromDate = SignalReportDataset.FirstDay.AddDays(2),
                ToDate = SignalReportDataset.FirstDay.AddDays(1),
            });

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        // Assert
        var count = await context
            .LinkAvailability
            .AsNoTracking()
            .CountAsync(
                period => period.TenantId == data.TenantId,
                CancellationToken.None);

        Assert.IsAssignableFrom<DbUpdateException>(error);
        Assert.Equal(8, count);
    }
}
