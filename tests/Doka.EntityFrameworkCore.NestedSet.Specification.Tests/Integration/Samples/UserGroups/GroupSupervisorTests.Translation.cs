using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Separates deferred candidate translation from materialized request selection.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>The actual candidate builder translates and executes without entity preloads (U06,U60).</summary>
    [Fact]
    public async Task CandidateBuilderExecutesAsOneDeferredScalarQuery()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]);
        probe.Enabled = true;

        // Act
        var rows = await App.SupervisorQueries.Candidates(context, data.TenantId, request)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new[] { data.Users[1], data.Users[2] }.Order(), rows.Select(row => row.CandidateUserId).Order());
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
    }
}
