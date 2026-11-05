namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

/// <summary>
/// Qualifies SQL Server compilation on every host while native SQL Server runtime tests remain in x64 CI.
/// </summary>
public sealed class SupervisorTranslationTests
{
    /// <summary>
    /// The actual status/evidence query compiles for each explicit person-selection alternative without opening a
    /// connection.
    /// </summary>
    /// <param name="mode">The explicit person-selection contract.</param>
    /// <param name="groupMode">The complete, first-group, or later-group selection contract.</param>
    [Theory]
    [InlineData("all", "all")]
    [InlineData("unique", "all")]
    [InlineData("priority", "all")]
    [InlineData("person", "all")]
    [InlineData("all", "eligible")]
    [InlineData("unique", "eligible")]
    [InlineData("priority", "eligible")]
    [InlineData("person", "eligible")]
    [InlineData("all", "type")]
    [InlineData("unique", "type")]
    [InlineData("priority", "type")]
    [InlineData("person", "type")]
    [InlineData("all", "eligible-later")]
    [InlineData("unique", "eligible-later")]
    [InlineData("priority", "eligible-later")]
    [InlineData("person", "eligible-later")]
    [InlineData("all", "type-later")]
    [InlineData("unique", "type-later")]
    [InlineData("priority", "type-later")]
    [InlineData("person", "type-later")]
    public async Task SqlServerCompilesTheActualStatusAndSelectionQuery(
        string mode,
        string groupMode
    )
    {
        // Arrange
        var options = ModelCompatibilityDatabase.Options<UserGroupContext>("SqlServer");
        await using var context = new UserGroupContext(options);
        var request = new SupervisorRequest
        {
            SourceUserId = 1,
            SearchStart = SearchStart.ParentsOnly,
            Roles = new EitherRole(Guid.NewGuid()),
            Groups = groupMode switch
            {
                "all" => new AllEligibleGroups(),
                "type" => new NthTypeGroupThenValidate(1, 1),
                "type-later" => new NthTypeGroupThenValidate(1, 2),
                "eligible-later" => new NthEligibleGroup([], 2),
                _ => new NthEligibleGroup([], 1),
            },
            Matches =
                [new MetadataMatch(MetadataOwner.SourceUser, "Route", MetadataOwner.CandidateMembership, "Route")],
            OnDay = new DateOnly(2026, 10, 16),
        };

        var selection = mode switch
        {
            "all" => SupervisorSelection.All,
            "priority" => SupervisorSelection.HighestBusinessPriority,
            "person" => SupervisorSelection.NthPerson(2),
            _ => SupervisorSelection.RequireUnique,
        };

        // Act
        var sql = SupervisorQueries
            .ResolutionRows(context, Guid.NewGuid(), request, selection)
            .ToQueryString();

        // Assert
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT_BIG", sql, StringComparison.Ordinal);
        Assert.NotEqual(
            ConnectionState.Open,
            context.Database.GetDbConnection()
                .State);

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
