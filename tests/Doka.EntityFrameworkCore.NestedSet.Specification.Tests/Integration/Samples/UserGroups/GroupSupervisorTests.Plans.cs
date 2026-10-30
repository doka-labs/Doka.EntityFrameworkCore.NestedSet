using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Captures real provider plans for the actual application joins and server-side person reduction.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// Plan evidence preserves the actual bindings and DTO-only command boundary for candidate and unique-selection
    /// shapes.
    /// </summary>
    /// <param name="unique">Whether to explain unique selection instead of the full candidate query.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualSupervisorQueriesProduceBoundedCommandAndPlanEvidence(bool unique)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CreateAsync(probe, planProbe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await SeedOriginProfileAsync(context, data, 16, 4);
        var request = data.Request(App.SearchStart.IncludeSelf) with
        {
            Groups = new App.NthTypeGroupThenValidate(4, 1),
            Matches = s_profileFields.Take(4).Select(field => new App.MetadataMatch(
                App.MetadataOwner.SourceUser, field, App.MetadataOwner.CandidateMembership, field)).ToArray(),
        };

        var commandIndex = planProbe.Commands.Count;
        probe.Enabled = true;

        // Act
        var rows = unique
            ? (await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
                App.SupervisorSelection.RequireUnique, CancellationToken.None)).Origins.Count
            : (await App.SupervisorQueries.Candidates(context, data.TenantId, request)
                .ToArrayAsync(CancellationToken.None)).Length;

        var readers = probe.Commands.Count;
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, commandIndex, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync(Engine + "-usergroups-" + (unique ? "unique" : "candidates"),
            [planProbe.Commands[commandIndex], plan]);

        // Assert
        Assert.Equal(16, rows);
        Assert.Equal(1, readers);
        Assert.False(string.IsNullOrWhiteSpace(plan));
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
