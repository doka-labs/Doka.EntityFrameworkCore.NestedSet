using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples.UserGroups;

/// <summary>Guards independent evidence admission and exact mixed-origin status transport.</summary>
public sealed partial class SupervisorEvidenceJoinTests
{
    /// <summary>Evidence is an independent inner join rather than a null-extended all-origin relation.</summary>
    /// <param name="mode">The person-selection rule whose witnesses admit complete occurrence evidence.</param>
    [Theory]
    [InlineData("unique")]
    [InlineData("priority")]
    [InlineData("person")]
    public async Task SqlServerEvidenceAdmissionIsIndependentOfMissingOriginMarkers(string mode)
    {
        // Arrange
        await using var context = new UserGroupContext(
            ModelCompatibilityDatabase.Options<UserGroupContext>("SqlServer"));

        var request = new SupervisorRequest
        {
            SourceUserId = 17,
            SearchStart = SearchStart.ParentsOnly,
            Groups = new AllEligibleGroups(),
            Roles = new MembershipRoleFilter(Guid.Parse("358bc287-e4ec-4413-93c4-23ec6dd4ef93")),
            Matches = [],
        };

        // Act
        var query = SupervisorQueries.ResolutionRows(context, Guid.NewGuid(), request, Selection(mode));
        var sql = query.ToQueryString();

        // Assert
        var evidenceUnion = Assert.IsAssignableFrom<MethodCallExpression>(query.Expression);
        Assert.Equal(nameof(Queryable.Concat), evidenceUnion.Method.Name);
        var statusUnion = Assert.IsAssignableFrom<MethodCallExpression>(evidenceUnion.Arguments[0]);
        Assert.Equal(nameof(Queryable.Concat), statusUnion.Method.Name);
        var evidence = Assert.IsAssignableFrom<MethodCallExpression>(evidenceUnion.Arguments[1]);
        Assert.Equal(nameof(Queryable.Join), evidence.Method.Name);
        Assert.Contains("UNION ALL", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
        Assert.NotEqual(ConnectionState.Open, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
        TestContext.Current.TestOutputHelper?.WriteLine(sql);
    }

    /// <summary>
    /// Missing and ambiguous origins use one marker each while a unique person's complete evidence stays bounded.
    /// </summary>
    /// <param name="mode">The person-selection contract, including ties and rule-specific continuation.</param>
    /// <param name="closed">Whether one intermediate edge excludes the farther occurrence only.</param>
    [Theory]
    [InlineData("unique", false)]
    [InlineData("priority", false)]
    [InlineData("person", false)]
    [InlineData("unique", true)]
    [InlineData("priority", true)]
    [InlineData("person", true)]
    public async Task MixedOriginsKeepExactStatusesOccurrencesAndPhysicalRows(string mode, bool closed)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var services = CreateServices(probe);
        await using var scope = services.CreateAsyncScope();
        await using var context = CreateContext(scope.ServiceProvider);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await PrepareMixedOriginsAsync(context, data, closed);
        var request = data.Request(SearchStart.IncludeSelf) with
        {
            Groups = new AllEligibleGroups(),
            Roles = new EitherRole(data.ManagerRoleId),
            Matches = [new(MetadataOwner.SourceMembership, "Route", MetadataOwner.CandidateMembership, "Route")],
        };

        var selection = Selection(mode);
        probe.Enabled = true;

        // Act
        var rows = await SupervisorQueries.ResolutionRows(context, data.TenantId, request, selection)
            .ToArrayAsync(CancellationToken.None);

        var result = SupervisorQueries.ReadResolution(data.TenantId, rows, selection);

        // Assert
        Assert.Equal(SupervisorBatchStatus.Completed, result.Status);
        Assert.Equal(3, result.Origins.Count);
        Assert.Single(rows, row => row.Kind == 0);
        Assert.Equal(closed ? 4 : 5, rows.Length);
        var unique = Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[5]);
        Assert.Equal(SupervisorResolutionStatus.Found, unique.Status);
        Assert.Equal(closed ? 1 : 2, unique.Evidence.Count);
        Assert.All(unique.Evidence, row =>
        {
            Assert.Equal(data.Users[1], row.CandidateUserId);
            Assert.Equal(data.TreeId, row.StartTreeId);
        });

        Assert.Equal(closed ? new[] { data.Groups[4] } : new[] { data.Groups[2], data.Groups[4] },
            unique.Evidence.Select(row => row.FoundGroupId).Order().ToArray());
        Assert.Equal(mode == "priority" && !closed ? data.Groups[2] : data.Groups[4],
            unique.Continuation!.FoundGroupId);
        var ambiguous = Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[6]);
        Assert.Equal(SupervisorResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Empty(ambiguous.Evidence);
        Assert.Null(ambiguous.Continuation);
        var absent = Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[0]);
        Assert.Equal(SupervisorResolutionStatus.NotFound, absent.Status);
        Assert.Empty(absent.Evidence);
        Assert.Null(absent.Continuation);
        Assert.Single(probe.Commands);
        Assert.InRange(probe.ReaderAdvances, rows.Length, rows.Length + 1);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A shared winner retains only the complete occurrences belonging to each exact origin.</summary>
    [Fact]
    public async Task SharedWinnerCannotMixEvidenceBetweenOriginPaths()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var services = CreateServices(probe);
        await using var scope = services.CreateAsyncScope();
        await using var context = CreateContext(scope.ServiceProvider);
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var data = await SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await PrepareMixedOriginsAsync(context, data, closed: false, sharedWinner: true);
        var request = data.Request(SearchStart.IncludeSelf) with
        {
            Groups = new AllEligibleGroups(),
            Roles = new EitherRole(data.ManagerRoleId),
            Matches = [new(MetadataOwner.SourceMembership, "Route", MetadataOwner.CandidateMembership, "Route")],
        };

        probe.Enabled = true;

        // Act
        var rows = await SupervisorQueries.ResolutionRows(context, data.TenantId, request,
                SupervisorSelection.RequireUnique)
            .ToArrayAsync(CancellationToken.None);

        var result = SupervisorQueries.ReadResolution(data.TenantId, rows, SupervisorSelection.RequireUnique);

        // Assert
        Assert.Equal(5, rows.Length);
        var first = Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[5]);
        Assert.Equal(SupervisorResolutionStatus.Found, first.Status);
        Assert.Equal(2, first.Evidence.Count);
        Assert.All(first.Evidence, row => Assert.Equal(data.Users[1], row.CandidateUserId));
        Assert.Equal(data.Groups[4], first.Continuation!.FoundGroupId);
        var second = Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[6]);
        Assert.Equal(SupervisorResolutionStatus.Found, second.Status);
        var occurrence = Assert.Single(second.Evidence);
        Assert.Equal(data.Users[1], occurrence.CandidateUserId);
        Assert.Equal(data.Groups[6], occurrence.FoundGroupId);
        Assert.Equal(data.Groups[6], second.Continuation!.FoundGroupId);
        Assert.Equal(SupervisorResolutionStatus.NotFound,
            Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[0]).Status);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Resolves only the three rules that server-reduce evidence to a unique person.</summary>
    private static SupervisorSelection Selection(string mode) => mode switch
    {
        "unique" => SupervisorSelection.RequireUnique,
        "priority" => SupervisorSelection.HighestBusinessPriority,
        "person" => SupervisorSelection.NthPerson(1),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
