using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SignalReportTests
{
    /// <summary>Empty and delimiter-ending choices retain exact owner equality, including significant spaces.</summary>
    /// <param name="nearest">Whether the matching classification must be the nearest owner.</param>
    /// <param name="selected">The exact catalog-authorized scalar requested by the caller.</param>
    /// <param name="stored">The scalar persisted on the moduleClass owner.</param>
    /// <param name="expected">Whether that owner exactly matches the authorized request.</param>
    [Theory]
    [InlineData(false, "", "", true)]
    [InlineData(true, "", "", true)]
    [InlineData(false, "", " ", false)]
    [InlineData(true, "", " ", false)]
    [InlineData(false, "Receiver|", "Receiver|", true)]
    [InlineData(true, "Receiver|", "Receiver|", true)]
    [InlineData(false, "Receiver|", "Receiver| ", false)]
    [InlineData(true, "Receiver|", "Receiver| ", false)]
    public async Task OwnerChoicesPreserveEmptyTextDelimitersAndSignificantSpaces(
        bool nearest,
        string selected,
        string stored,
        bool expected
    )
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
            Value = selected,
        });
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        await context.TagValues.Where(value => value.TenantId == data.TenantId
                && value.NodeId == SignalReportDataset.Id(3) && value.FieldCode == "ModuleClass")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Value, stored), CancellationToken.None);
        var request = SingleAnchor(data) with
        {
            Criteria =
            [
                new TagCriterion(SignalReportDataset.ModuleKind, "ModuleClass", selected,
                    nearest ? TagOwnerMode.Nearest : TagOwnerMode.Any),
            ],
        };

        probe.Reset();

        // Act
        var cells = await SignalReportBuilder.ReadAsync(context, request, CancellationToken.None);

        // Assert
        var cell = Assert.Single(cells);
        Assert.Equal(expected, cell.HasData);
        // WHY: The independent fixture contributes 10 + 20 + 40 daily deployments; the final 50 belongs
        // to an expired direct edge. These counts do not come from the report or comparison implementation.
        Assert.Equal(expected ? 70d : 0d, cell.Value);
        AssertReportBoundary(context, probe);
    }
}
