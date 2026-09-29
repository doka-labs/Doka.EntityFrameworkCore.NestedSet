namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Checks tenant integrity and physical deletion behavior of the sample's explicit membership table.</summary>
public sealed class UserGroupMembershipTests
{
    /// <summary>A user may have two groups while another user in the same supervisor tree has none.</summary>
    [Fact]
    public async Task UsersCanHaveZeroOrMultipleGroupsAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tenantId = Guid.NewGuid();
        var groupTree = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        var firstGroup = new UserGroup { Name = "Engineering" };
        var secondGroup = new UserGroup { Name = "Finance" };
        await groupTree.InsertRootAsync(firstGroup, Guid.NewGuid(), CancellationToken.None);
        await groupTree.InsertRootAsync(secondGroup, Guid.NewGuid(), CancellationToken.None);
        var userTree = context
            .NestedSet<User>()
            .ForScope(tenantId);

        var assignedUser = new User
        {
            Name = "Taylor",
            Position = "Engineer",
        };

        var unassignedUser = new User
        {
            Name = "Morgan",
            Position = "Chief executive",
        };

        await userTree.InsertRootAsync(unassignedUser, Guid.NewGuid(), CancellationToken.None);
        await userTree.InsertChildAsync(assignedUser, unassignedUser.Id, CancellationToken.None);

        // Act
        context.UserGroupMemberships.AddRange(
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = assignedUser.Id,
                UserGroupId = firstGroup.Id
            },
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = assignedUser.Id,
                UserGroupId = secondGroup.Id
            });

        await context.SaveChangesAsync(CancellationToken.None);
        var assignedGroups = await context
            .UserGroupMemberships
            .AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.UserId == assignedUser.Id)
            .OrderBy(link => link.UserGroupId)
            .Select(link => link.UserGroupId)
            .ToArrayAsync(CancellationToken.None);

        var unassignedCount = await context
            .UserGroupMemberships
            .AsNoTracking()
            .CountAsync(link => link.TenantId == tenantId && link.UserId == unassignedUser.Id, CancellationToken.None);

        // Assert
        Assert.Equal(new[] { firstGroup.Id, secondGroup.Id }.OrderBy(id => id), assignedGroups);
        Assert.Equal(0, unassignedCount);
    }

    /// <summary>The composite primary key rejects a repeated user-to-group association.</summary>
    [Fact]
    public async Task DuplicateMembershipFailsAtDatabaseBoundaryAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tenantId = Guid.NewGuid();
        var group = new UserGroup { Name = "Engineering" };
        var user = new User
        {
            Name = "Taylor",
            Position = "Engineer",
        };

        await context
            .NestedSet<UserGroup>()
            .ForScope(tenantId)
            .InsertRootAsync(group, Guid.NewGuid(), CancellationToken.None);

        await context
            .NestedSet<User>()
            .ForScope(tenantId)
            .InsertRootAsync(user, Guid.NewGuid(), CancellationToken.None);

        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = user.Id,
                UserGroupId = group.Id,
            });

        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"UserGroupMemberships\" (\"TenantId\", \"UserId\", \"UserGroupId\") VALUES ({tenantId}, {user.Id}, {group.Id})",
            CancellationToken.None));

        var persistedCount = await context
            .UserGroupMemberships
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.IsType<SqliteException>(exception, exactMatch: false);
        Assert.Equal(1, persistedCount);
    }

    /// <summary>The composite user foreign key rejects a link that names a user from another tenant.</summary>
    [Fact]
    public async Task ForeignTenantUserMembershipFailsAtDatabaseBoundaryAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var group = new UserGroup { Name = "Engineering" };
        var foreignUser = new User
        {
            Name = "Taylor",
            Position = "Engineer",
        };

        await context
            .NestedSet<UserGroup>()
            .ForScope(firstTenant)
            .InsertRootAsync(group, Guid.NewGuid(), CancellationToken.None);

        await context
            .NestedSet<User>()
            .ForScope(secondTenant)
            .InsertRootAsync(foreignUser, Guid.NewGuid(), CancellationToken.None);

        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = firstTenant,
                UserId = foreignUser.Id,
                UserGroupId = group.Id,
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

    /// <summary>Removing one association leaves the user and the user's other group intact.</summary>
    [Fact]
    public async Task RemovingMembershipKeepsOtherGroupAndUserAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tenantId = Guid.NewGuid();
        var groupTree = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        var firstGroup = new UserGroup { Name = "Engineering" };
        var secondGroup = new UserGroup { Name = "Finance" };
        var user = new User
        {
            Name = "Taylor",
            Position = "Engineer",
        };

        await groupTree.InsertRootAsync(firstGroup, Guid.NewGuid(), CancellationToken.None);
        await groupTree.InsertRootAsync(secondGroup, Guid.NewGuid(), CancellationToken.None);
        await context
            .NestedSet<User>()
            .ForScope(tenantId)
            .InsertRootAsync(user, Guid.NewGuid(), CancellationToken.None);
        var removed = new UserGroupMembership
        {
            TenantId = tenantId,
            UserId = user.Id,
            UserGroupId = firstGroup.Id,
        };

        context.UserGroupMemberships.AddRange(
            removed,
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = user.Id,
                UserGroupId = secondGroup.Id,
            });

        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        context.UserGroupMemberships.Remove(removed);
        await context.SaveChangesAsync(CancellationToken.None);
        var remainingGroupIds = await context
            .UserGroupMemberships
            .AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.UserId == user.Id)
            .Select(link => link.UserGroupId)
            .ToArrayAsync(CancellationToken.None);

        var userExists = await context
            .Users
            .AsNoTracking()
            .AnyAsync(member => member.TenantId == tenantId && member.Id == user.Id, CancellationToken.None);

        // Assert
        Assert.Equal([secondGroup.Id], remainingGroupIds);
        Assert.True(userExists);
    }

    /// <summary>Deleting a user through NestedSet also deletes its memberships in the database.</summary>
    [Fact]
    public async Task DeletingUserSubtreeCascadesMembershipsAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tenantId = Guid.NewGuid();
        var group = new UserGroup { Name = "Engineering" };
        var user = new User
        {
            Name = "Taylor",
            Position = "Engineer"
        };

        await context
            .NestedSet<UserGroup>()
            .ForScope(tenantId)
            .InsertRootAsync(group, Guid.NewGuid(), CancellationToken.None);

        var users = context
            .NestedSet<User>()
            .ForScope(tenantId);

        await users.InsertRootAsync(user, Guid.NewGuid(), CancellationToken.None);
        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = user.Id,
                UserGroupId = group.Id,
            });

        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        await users.DeleteSubtreeAsync(user.Id, CancellationToken.None);
        var remainingUsers = await context
            .Users
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        var remainingMemberships = await context
            .UserGroupMemberships
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, remainingUsers);
        Assert.Equal(0, remainingMemberships);
    }

    /// <summary>A group with members cannot be removed without an explicit application decision about those links.</summary>
    [Fact]
    public async Task DeletingGroupWithMembershipIsRestrictedAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tenantId = Guid.NewGuid();
        var groups = context
            .NestedSet<UserGroup>()
            .ForScope(tenantId);

        var group = new UserGroup { Name = "Engineering" };
        var user = new User
        {
            Name = "Taylor",
            Position = "Engineer",
        };

        await groups.InsertRootAsync(group, Guid.NewGuid(), CancellationToken.None);
        await context
            .NestedSet<User>()
            .ForScope(tenantId)
            .InsertRootAsync(user, Guid.NewGuid(), CancellationToken.None);

        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = user.Id,
                UserGroupId = group.Id,
            });

        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(() => groups.DeleteSubtreeAsync(group.Id, CancellationToken.None));
        var remainingGroups = await context
            .Groups
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        var remainingMemberships = await context
            .UserGroupMemberships
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.IsType<SqliteException>(exception, exactMatch: false);
        Assert.Equal(1, remainingGroups);
        Assert.Equal(1, remainingMemberships);
    }

    /// <summary>Creates the sample context with the same nested-set extension as the console application.</summary>
    private static UserGroupSqliteContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<UserGroupSqliteContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        return new UserGroupSqliteContext(options);
    }
}
