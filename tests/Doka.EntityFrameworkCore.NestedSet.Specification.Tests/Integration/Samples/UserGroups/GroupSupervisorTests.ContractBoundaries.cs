using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Executes consumer rules whose correctness depends on the real provider predicate boundary.</summary>
public abstract partial class GroupSupervisorTests
{
    /// <summary>Database padding cannot turn an unsupported metadata kind into the supported Text contract.</summary>
    [Fact]
    public async Task PaddedMetadataKindIsRejectedByTheActualCatalogReader()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.MembershipMetadataDefinitions.Where(definition => definition.TenantId == data.TenantId
                && definition.FieldCode == "Route")
            .ExecuteUpdateAsync(setters => setters.SetProperty(definition => definition.ValueKind, "Text "),
                CancellationToken.None);
        var request = data.Request(group: data.Groups[5]);
        probe.Enabled = true;

        // Act
        var error = await Record.ExceptionAsync(() => App.SupervisorQueries.ResolveAsync(context,
            data.TenantId, request, App.SupervisorSelection.RequireUnique, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Exact catalog codes reject padding aliases either through the physical FK or the actual query residual.
    /// </summary>
    [Fact]
    public async Task PaddedStoredFieldCodeCannotBecomeARoutingMatch()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.NthTypeGroupThenValidate(3, 1) };
        probe.Enabled = true;

        // Act
        var writeError = await Record.ExceptionAsync(() => context.MembershipMetadatas
            .Where(value => value.TenantId == data.TenantId && value.UserGroupId == data.Groups[4]
                && value.FieldCode == "Route")
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.FieldCode, "Route "), CancellationToken.None));

        var rows = writeError is null
            ? await App.SupervisorQueries.Candidates(context, data.TenantId, request)
                .ToArrayAsync(CancellationToken.None)
            : [];

        // Assert
        Assert.Empty(rows);
        if (writeError is null)
        {
            Assert.Single(probe.Commands);
        }
        else
        {
            Assert.IsAssignableFrom<DbException>(writeError);
            Assert.Contains("foreign", writeError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(probe.Commands);
        }
    }

    /// <summary>Structural distance counts edges, not eligible groups, people, or supervisor hops (U36).</summary>
    [Fact]
    public async Task StructuralDistanceTwoCanSelectAGroupWithoutAManager()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        probe.Enabled = true;

        // Act
        var groups = await context.NestedSet<App.UserGroup>()
            .ForScope(data.TenantId)
            .SelectAncestorsOrSelf(group => group.Id == data.Groups[5],
                (start, ancestor, distance) => new
                {
                    ancestor.Id,
                    Distance = distance,
                    HasManager = context.MembershipRoles.Any(role => role.TenantId == data.TenantId
                        && role.UserGroupId == ancestor.Id && role.RoleId == data.ManagerRoleId),
                })
            .Where(row => row.Distance == 2)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(data.Groups[3], Assert.Single(groups).Id);
        Assert.Equal(2, groups[0].Distance);
        Assert.False(groups[0].HasManager);
        Assert.Single(probe.Commands);
        Assert.Equal(0, probe.Entities);
    }

    /// <summary>Eligible types exclude a nearer roleholder before assigning the first group rank (U14).</summary>
    [Fact]
    public async Task EligibleTypeFilterSkipsNearerPeopleOfAnotherGroupType()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with { Groups = new App.NthEligibleGroup([1], 1) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        var row = Assert.Single(Assert.Single(result.Origins).Evidence);
        Assert.Equal(data.Users[3], row.CandidateUserId);
        Assert.Equal(data.Groups[2], row.FoundGroupId);
        Assert.Single(probe.Commands);
    }

    /// <summary>Hidden memberships and grants cannot consume an otherwise valid nearest eligible group (U49).</summary>
    /// <param name="membership">Whether the membership itself is hidden instead of its local role grant.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenMembershipOrLocalGrantIsFilteredBeforeGroupRank(bool membership)
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        if (membership)
        {
            await context.UserGroupMemberships.Where(row => row.TenantId == data.TenantId
                    && row.UserGroupId == data.Groups[4])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Visible, false), CancellationToken.None);
        }
        else
        {
            await context.MembershipRoles.Where(row => row.TenantId == data.TenantId
                    && row.UserGroupId == data.Groups[4])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Visible, false), CancellationToken.None);
        }

        var request = data.Request(group: data.Groups[5]);
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(data.Users[3], Assert.Single(Assert.Single(result.Origins).Evidence).CandidateUserId);
        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// An expired direct edge blocks its origin despite a shortcut or global grant, while another origin stays valid
    /// (U51).
    /// </summary>
    [Fact]
    public async Task ClosedEdgeCannotBeBypassedAndDoesNotBlockAnotherOwnOrigin()
    {
        // Arrange
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        await context.GroupRelationPeriods.Where(row => row.TenantId == data.TenantId
                && row.ChildId == data.Groups[5])
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ToDate, new DateOnly(2026, 10, 15)),
                CancellationToken.None);
        await context.GroupRelationPeriods.AddAsync(new App.GroupRelationPeriod
        {
            TenantId = data.TenantId,
            ParentId = data.Groups[0],
            ChildId = data.Groups[5],
            FromDate = new DateOnly(2026, 1, 1),
        }, CancellationToken.None);
        await context.UserRoles.AddAsync(new App.UserRole
        {
            TenantId = data.TenantId,
            UserId = data.Users[1],
            RoleId = data.ManagerRoleId,
        }, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var request = data.Request(App.SearchStart.IncludeSelf) with { Roles = new App.EitherRole(data.ManagerRoleId) };
        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.ResolveAsync(context, data.TenantId, request,
            App.SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorResolutionStatus.NotFound,
            result.Origins.Single(origin => origin.StartGroupId == data.Groups[5]).Status);
        Assert.Equal(data.Users[6], Assert.Single(result.Origins.Single(origin =>
            origin.StartGroupId == data.Groups[6]).Evidence).CandidateUserId);
        Assert.Single(probe.Commands);
    }

    /// <summary>
    /// Request scalar bindings stay parameters, rather than creating request-specific SQL or query-cache shapes
    /// (U28).
    /// </summary>
    [Fact]
    public async Task RoutingRequestScalarsAreBoundAsCommandParameters()
    {
        // Arrange
        const int routingUser = 923456789;
        const string field = "ChannelClass";
        var probe = new SupervisorCommandProbe();
        await using var context = await CreateAsync(probe);
        var data = await App.SupervisorScenarioData.CreateAsync(context, Guid.NewGuid(), CancellationToken.None);
        var request = data.Request(group: data.Groups[5]) with
        {
            RoutingUserId = routingUser,
            Matches = [new App.MetadataMatch(App.MetadataOwner.SourceUser, field,
                App.MetadataOwner.CandidateMembership, field)],
        };

        probe.Enabled = true;

        // Act
        var result = await App.SupervisorQueries.Candidates(context, data.TenantId, request)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(result);
        var command = Assert.Single(probe.Commands);
        Assert.DoesNotContain("923456789", command, StringComparison.Ordinal);
        Assert.DoesNotContain(field, command, StringComparison.Ordinal);
        Assert.Contains(routingUser, probe.ParameterValues);
        Assert.True(probe.HasStringParameterElement(field));
        Assert.Equal(0, probe.Entities);
    }
}
