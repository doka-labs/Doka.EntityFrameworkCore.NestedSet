namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Verifies the single initial migration owned by each UserGroups provider context.</summary>
public sealed class UserGroupsInitialMigrationTests
{
    /// <summary>Both provider contexts expose exactly one initial migration for the final sample model.</summary>
    [Fact]
    public void EachProviderHasOneInitialMigration()
    {
        // Arrange
        var dokaOptions = new DbContextOptionsBuilder<UserGroupContext>()
            .UseMySql(
                "Server=127.0.0.1;Port=1;Database=nestedset_sample_usergroups;User ID=unused;Password=unused;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .UseNestedSets()
            .Options;

        var sqliteOptions = new DbContextOptionsBuilder<UserGroupSqliteContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        using var doka = new UserGroupContext(dokaOptions);
        using var sqlite = new UserGroupSqliteContext(sqliteOptions);

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
        Assert.EndsWith("_InitialUserGroups", Assert.Single(dokaMigrations), StringComparison.Ordinal);
        Assert.EndsWith("_InitialUserGroups", Assert.Single(sqliteMigrations), StringComparison.Ordinal);
    }

    /// <summary>The fresh SQLite migration supports both nested sets and multiple memberships in one tenant.</summary>
    [Fact]
    public async Task InitialMigrationCreatesBothHierarchiesAndMembershipsAsync()
    {
        // Arrange
        await using var context = CreateContext();
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
        Assert.EndsWith("_InitialUserGroups", Assert.Single(applied), StringComparison.Ordinal);
    }

    /// <summary>The migrated join table rejects a group belonging to another tenant.</summary>
    [Fact]
    public async Task MigratedMembershipRejectsForeignTenantGroupAsync()
    {
        // Arrange
        await using var context = CreateContext();
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

    /// <summary>Creates the SQLite context using the sample's ordinary provider and NestedSet registration.</summary>
    private static UserGroupSqliteContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<UserGroupSqliteContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        return new UserGroupSqliteContext(options);
    }
}
