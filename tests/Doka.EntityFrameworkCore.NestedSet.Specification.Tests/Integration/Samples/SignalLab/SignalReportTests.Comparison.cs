using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>SQL padding cannot turn a different stored scalar into a known ordinal request choice.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrailingValueSpacesDoNotMatchAKnownTagChoice(bool nearest)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        await context.TagValues.Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(3) && value.FieldCode == "ModuleClass")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, "Receiver "), CancellationToken.None);
        var request = SingleAnchor(data) with
        {
            Criteria =
            [
                new TagCriterion(SignalReportDataset.ModuleKind, "ModuleClass", "Receiver",
                    nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any),
            ],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.False(Assert.Single(cells).HasData);
        AssertReportBoundary(context, probe);
    }

    /// <summary>A padded field alias accepted by a provider FK cannot satisfy another exact request field.</summary>
    [EngineTheory(
        ExcludedEngines = ["PostgreSql", "Sqlite"],
        Reason = "PostgreSQL and SQLite already reject this padded field alias "
            + "through their exact-text definition FK.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrailingFieldSpacesDoNotAliasAKnownMetadataField(bool nearest)
    {
        // Arrange
        var probe = new SignalReportProbe();
        await using var context = await CreateContextAsync(probe);
        var data = await SignalReportDataset.CreateAsync(context, 7, false, CancellationToken.None, Guid.NewGuid());
        await context.TagValues.Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(3) && value.FieldCode == "ModuleClass")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.FieldCode, "ModuleClass "),
                CancellationToken.None);
        var request = SingleAnchor(data) with
        {
            Criteria =
            [
                new TagCriterion(SignalReportDataset.ModuleKind, "ModuleClass", "Receiver",
                    nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any),
            ],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        Assert.False(Assert.Single(cells).HasData);
        AssertReportBoundary(context, probe);
    }
}
