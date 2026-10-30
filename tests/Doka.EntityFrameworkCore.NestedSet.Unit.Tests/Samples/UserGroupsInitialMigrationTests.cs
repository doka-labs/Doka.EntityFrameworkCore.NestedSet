namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>
/// Verifies the preserved initial migration and additive query migration owned by each UserGroups provider context.
/// </summary>
public sealed class UserGroupsInitialMigrationTests
{
    /// <summary>The additive migration preserves the visibility and memberships of released-schema rows.</summary>
    [Fact]
    public async Task FeatureMigrationPreservesExistingVisibleApplicationRows()
    {
        // Arrange
        var options = SqliteOptions();
        await using var services = SampleTestServices.Create(options);
        await using var context = new UserGroupSqliteContext(
            new DbContextOptionsBuilder<UserGroupSqliteContext>(options).UseInternalServiceProvider(services).Options);

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        var initial = context.Database.GetMigrations().First();
        await context.GetService<IMigrator>().MigrateAsync(initial, CancellationToken.None);
        var tenant = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var role = Guid.NewGuid();
        var timestamp = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "UserGroups"
                ("Id", "TenantId", "TreeId", "ParentId", "Name", "Left", "Right", "Depth", "Position", "UpdatedAtUtc")
                VALUES (1, {tenant}, {tree}, NULL, 'Existing team', 1, 2, 0, 0, {timestamp});
            INSERT INTO "Users"
                ("Id", "TenantId", "SupervisorId", "Name", "Position", "Depth", "Left", "Right",
                 "NestedSetPosition", "TreeId")
                VALUES (1, {tenant}, NULL, 'Existing user', 'Engineer', 0, 1, 2, 0, {tree});
            INSERT INTO "Roles" ("Id", "TenantId", "Name", "UpdatedAtUtc")
                VALUES ({role}, {tenant}, 'Existing role', {timestamp});
            INSERT INTO "UserGroupMemberships" ("TenantId", "UserId", "UserGroupId") VALUES ({tenant}, 1, 1);
            """, CancellationToken.None);

        // Act
        await context.Database.MigrateAsync(CancellationToken.None);
        var user = await context.Users.AsNoTracking().SingleAsync(CancellationToken.None);
        var group = await context.Groups.AsNoTracking().SingleAsync(CancellationToken.None);
        var savedRole = await context.Roles.AsNoTracking().SingleAsync(CancellationToken.None);
        var membership = await context.UserGroupMemberships.AsNoTracking().SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Existing user", user.Name);
        Assert.True(user.Visible);
        Assert.Equal("Existing team", group.Name);
        Assert.True(group.Visible);
        Assert.Null(group.TypeId);
        Assert.Equal(role, savedRole.Id);
        Assert.True(savedRole.Visible);
        Assert.True(membership.Visible);
        Assert.Equal(0, membership.LeadershipPriority);
    }

    /// <summary>
    /// Both provider contexts preserve the released initial migration and add exactly one query feature migration.
    /// </summary>
    [Fact]
    public void EachProviderPreservesInitialAndAddsOneFeatureMigration()
    {
        // Arrange
        var dokaOptions = new DbContextOptionsBuilder<UserGroupContext>()
            .UseMySql(
                "Server=127.0.0.1;Port=1;Database=nestedset_sample_usergroups;User ID=unused;Password=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)), provider => provider.UseNestedSets())
            .Options;

        var sqliteOptions = new DbContextOptionsBuilder<UserGroupSqliteContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        using var dokaServices = SampleTestServices.Create(dokaOptions);
        using var sqliteServices = SampleTestServices.Create(sqliteOptions);
        using var doka = new UserGroupContext(
            new DbContextOptionsBuilder<UserGroupContext>(dokaOptions)
                .UseInternalServiceProvider(dokaServices).Options);

        using var sqlite = new UserGroupSqliteContext(
            new DbContextOptionsBuilder<UserGroupSqliteContext>(sqliteOptions)
                .UseInternalServiceProvider(sqliteServices).Options);

        // Act
        var dokaMigrations = doka
            .Database
            .GetMigrations()
            .ToArray();

        var sqliteMigrations = sqlite
            .Database
            .GetMigrations()
            .ToArray();

        // Assert
        Assert.Equal(2, dokaMigrations.Length);
        Assert.EndsWith("_InitialUserGroups", dokaMigrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_GroupSupervisorQueries", dokaMigrations[1], StringComparison.Ordinal);
        Assert.Equal(2, sqliteMigrations.Length);
        Assert.EndsWith("_InitialUserGroups", sqliteMigrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_GroupSupervisorQueries", sqliteMigrations[1], StringComparison.Ordinal);
    }

    /// <summary>The fresh SQLite migration supports both nested sets and multiple memberships in one tenant.</summary>
    [Fact]
    public async Task InitialMigrationCreatesBothHierarchiesAndMembershipsAsync()
    {
        // Arrange
        var options = SqliteOptions();
        await using var services = SampleTestServices.Create(options);
        await using var context = new UserGroupSqliteContext(
            new DbContextOptionsBuilder<UserGroupSqliteContext>(options).UseInternalServiceProvider(services).Options);

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.MigrateAsync(CancellationToken.None);
        var tenantId = Guid.NewGuid();
        var groupTreeId = Guid.NewGuid();
        var userTreeId = Guid.NewGuid();
        var groups = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        var firstGroup = new UserGroup { Name = "Engineering" };
        var secondGroup = new UserGroup { Name = "Finance" };
        var users = context
            .NestedSet<User>()
            .ForScope(tenantId);

        var manager = new User
        {
            Name = "Morgan",
            Position = "Director",
        };

        var employee = new User
        {
            Name = "Taylor",
            Position = "Engineer",
        };

        // Act
        await groups.InsertRootAsync(firstGroup, groupTreeId, CancellationToken.None);
        await groups.InsertChildAsync(secondGroup, firstGroup.Id, CancellationToken.None);
        await users.InsertRootAsync(manager, userTreeId, CancellationToken.None);
        await users.InsertChildAsync(employee, manager.Id, CancellationToken.None);
        context.UserGroupMemberships.AddRange(
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = employee.Id,
                UserGroupId = firstGroup.Id
            },
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = employee.Id,
                UserGroupId = secondGroup.Id
            });

        await context.SaveChangesAsync(CancellationToken.None);

        var memberships = await context
            .UserGroupMemberships
            .AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.UserId == employee.Id)
            .OrderBy(link => link.UserGroupId)
            .Select(link => link.UserGroupId)
            .ToArrayAsync(CancellationToken.None);

        var groupReport = await groups
            .InTree(groupTreeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        var userReport = await users
            .InTree(userTreeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new[] { firstGroup.Id, secondGroup.Id }.OrderBy(id => id), memberships);
        Assert.True(groupReport.IsValid);
        Assert.True(userReport.IsValid);
        Assert.Equal(2, applied.Count());
        Assert.EndsWith("_GroupSupervisorQueries", applied.Last(), StringComparison.Ordinal);
    }

    /// <summary>The migrated join table rejects a group belonging to another tenant.</summary>
    [Fact]
    public async Task MigratedMembershipRejectsForeignTenantGroupAsync()
    {
        // Arrange
        var options = SqliteOptions();
        await using var services = SampleTestServices.Create(options);
        await using var context = new UserGroupSqliteContext(
            new DbContextOptionsBuilder<UserGroupSqliteContext>(options).UseInternalServiceProvider(services).Options);

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.MigrateAsync(CancellationToken.None);
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var foreignGroup = new UserGroup { Name = "Other tenant" };
        var user = new User
        {
            Name = "Taylor",
            Position = "Engineer",
        };

        await context
            .NestedSet<UserGroup>()
            .ForScope(secondTenant)
            .InsertRootAsync(foreignGroup, Guid.NewGuid(), CancellationToken.None);

        await context
            .NestedSet<User>()
            .ForScope(firstTenant)
            .InsertRootAsync(user, Guid.NewGuid(), CancellationToken.None);

        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = firstTenant,
                UserId = user.Id,
                UserGroupId = foreignGroup.Id,
            });

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var persistedCount = await context
            .UserGroupMemberships
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DbUpdateException>(exception, exactMatch: false);
        Assert.Equal(0, persistedCount);
    }

    /// <summary>Preserves the sample's ordinary SQLite provider and NestedSet registration.</summary>
    private static DbContextOptions<UserGroupSqliteContext> SqliteOptions()
        => new DbContextOptionsBuilder<UserGroupSqliteContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;
}
