namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Guards the UserGroups sample's independent supervisor tree and tenant-owned memberships.</summary>
public sealed class UserSupervisorSampleTests
{
    /// <summary>Distinguishes the public business Position from the tree identity and coordinates in shadow state.</summary>
    [Fact]
    public void StructuralCoordinatesRemainShadowProperties()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var entity = context.Model.FindEntityType(typeof(User))!;
        var position = entity.FindProperty(nameof(User.Position))!;
        var treeId = entity.FindProperty(UserHierarchyProperties.TreeId)!;
        var left = entity.FindProperty(UserHierarchyProperties.Left)!;
        var right = entity.FindProperty(UserHierarchyProperties.Right)!;
        var siblingPosition = entity.FindProperty(UserHierarchyProperties.NestedSetPosition)!;
        var membership = context.Model.FindEntityType(typeof(UserGroupMembership))!;

        // Assert
        Assert.Equal(typeof(string), position.ClrType);
        Assert.False(position.IsShadowProperty());
        Assert.True(treeId.IsShadowProperty());
        Assert.Equal(typeof(Guid), treeId.ClrType);
        Assert.False(treeId.IsNullable);
        Assert.True(left.IsShadowProperty());
        Assert.True(right.IsShadowProperty());
        Assert.True(siblingPosition.IsShadowProperty());
        Assert.All([left, right, siblingPosition], property => Assert.Equal(typeof(long), property.ClrType));
        Assert.Null(typeof(User).GetProperty(UserHierarchyProperties.TreeId));
        Assert.Null(typeof(User).GetProperty(UserHierarchyProperties.Left));
        Assert.Null(typeof(User).GetProperty(UserHierarchyProperties.Right));
        Assert.Null(typeof(User).GetProperty(UserHierarchyProperties.NestedSetPosition));
        Assert.Null(typeof(User).GetProperty(nameof(UserGroupMembership.UserGroupId)));
        Assert.Equal(
            [
                nameof(UserGroupMembership.TenantId),
                nameof(UserGroupMembership.UserId),
                nameof(UserGroupMembership.UserGroupId)
            ],
            membership.FindPrimaryKey()!.Properties.Select(property => property.Name));

        Assert.Contains(
            membership.GetIndexes(),
            index => index
                .Properties
                .Select(property => property.Name)
                .SequenceEqual(
                [
                    nameof(UserGroupMembership.TenantId),
                    nameof(UserGroupMembership.UserGroupId),
                    nameof(UserGroupMembership.UserId)
                ]));

        Assert.Contains(
            membership.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User)
                && foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(
            membership.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(UserGroup)
                && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(
            entity.GetIndexes(),
            index => index
                .Properties
                .Select(property => property.Name)
                .SequenceEqual(
                [
                    nameof(User.TenantId),
                    UserHierarchyProperties.TreeId,
                    nameof(User.SupervisorId),
                    UserHierarchyProperties.NestedSetPosition,
                ]));
    }

    /// <summary>A moved employee is found through scoped ancestors and retains its group membership.</summary>
    [Fact]
    public async Task SupervisorMoveChangesShadowBoundsWithoutChangingBusinessPositionAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tenantId = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var group = new UserGroup { Name = "Engineering" };
        await context
            .NestedSet<UserGroup>()
            .ForScope(tenantId)
            .InsertRootAsync(group, Guid.NewGuid(), CancellationToken.None);

        var chief = new User
        {
            Name = "Chief",
            Position = "Chief executive",
        };

        var oldManager = new User
        {
            Name = "Old",
            Position = "Director",
        };

        var newManager = new User
        {
            Name = "New",
            Position = "Director",
        };

        var employee = new User
        {
            Name = "Employee",
            Position = "Engineer",
        };

        var hierarchy = context
            .NestedSet<User>()
            .ForScope(tenantId);

        await hierarchy.InsertRootAsync(chief, treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(oldManager, chief.Id, CancellationToken.None);
        await hierarchy.InsertChildAsync(employee, oldManager.Id, CancellationToken.None);
        await hierarchy.InsertChildAsync(newManager, chief.Id, CancellationToken.None);
        context.UserGroupMemberships.Add(
            new UserGroupMembership
            {
                TenantId = tenantId,
                UserId = employee.Id,
                UserGroupId = group.Id,
            });

        await context.SaveChangesAsync(CancellationToken.None);

        // Act
        await hierarchy.MoveToAsync(employee.Id, newManager.Id, CancellationToken.None);
        var director = await hierarchy
            .AncestorsOf(employee.Id)
            .Where(user => user.Position == "Director")
            .SingleAsync(CancellationToken.None);

        var moved = await hierarchy
            .InTree(treeId)
            .Nodes
            .Where(user => user.Id == employee.Id)
            .Select(user => new
            {
                user.Position,
                user.SupervisorId,
                TreeId = EF.Property<Guid>(user, UserHierarchyProperties.TreeId),
                Left = EF.Property<long>(user, UserHierarchyProperties.Left),
                Right = EF.Property<long>(user, UserHierarchyProperties.Right),
                SiblingPosition = EF.Property<long>(user, UserHierarchyProperties.NestedSetPosition),
            })
            .SingleAsync(CancellationToken.None);

        var report = await hierarchy
            .InTree(treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        var memberships = await context
            .UserGroupMemberships
            .Where(link => link.TenantId == tenantId && link.UserId == employee.Id)
            .Select(link => link.UserGroupId)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(newManager.Id, director.Id);
        Assert.Equal("Engineer", moved.Position);
        Assert.Equal(newManager.Id, moved.SupervisorId);
        Assert.Equal([group.Id], memberships);
        Assert.Equal(treeId, moved.TreeId);
        Assert.Equal((5L, 6L, 0L), (moved.Left, moved.Right, moved.SiblingPosition));
        Assert.True(report.IsValid);
    }

    /// <summary>A supervisor in another tenant cannot become the anchor of a scoped move.</summary>
    [Fact]
    public async Task ForeignTenantSupervisorIsRejectedWithoutChangingStructureAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var treeId = Guid.NewGuid();
        var firstGroup = new UserGroup { Name = "First group" };
        var secondGroup = new UserGroup { Name = "Second group" };
        await context
            .NestedSet<UserGroup>()
            .ForScope(firstTenant)
            .InsertRootAsync(firstGroup, Guid.NewGuid(), CancellationToken.None);

        await context
            .NestedSet<UserGroup>()
            .ForScope(secondTenant)
            .InsertRootAsync(secondGroup, Guid.NewGuid(), CancellationToken.None);

        var firstRoot = new User
        {
            Name = "First root",
            Position = "Director",
        };

        var child = new User
        {
            Name = "Child",
            Position = "Engineer",
        };

        var foreignRoot = new User
        {
            Name = "Foreign root",
            Position = "Director",
        };

        var firstHierarchy = context
            .NestedSet<User>()
            .ForScope(firstTenant);

        await firstHierarchy.InsertRootAsync(firstRoot, treeId, CancellationToken.None);
        await firstHierarchy.InsertChildAsync(child, firstRoot.Id, CancellationToken.None);
        await context
            .NestedSet<User>()
            .ForScope(secondTenant)
            .InsertRootAsync(foreignRoot, treeId, CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(() => firstHierarchy.MoveToAsync(
            child.Id,
            foreignRoot.Id,
            CancellationToken.None));

        var persisted = await firstHierarchy
            .InTree(treeId)
            .Nodes
            .Where(user => user.Id == child.Id)
            .Select(user => new
            {
                user.SupervisorId,
                Left = EF.Property<long>(user, UserHierarchyProperties.Left),
                Right = EF.Property<long>(user, UserHierarchyProperties.Right),
            })
            .SingleAsync(CancellationToken.None);

        // Assert
        var rejected = Assert.IsType<NestedSetException>(exception, exactMatch: false);
        Assert.Equal(NestedSetErrorCode.NodeNotFound, rejected.Code);
        Assert.Equal((firstRoot.Id, 2L, 3L), (persisted.SupervisorId, persisted.Left, persisted.Right));
    }

    /// <summary>The relational membership key rejects a group from a different tenant.</summary>
    [Fact]
    public async Task ForeignTenantGroupMembershipIsRejectedAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var firstGroup = new UserGroup { Name = "First group" };
        var foreignGroup = new UserGroup { Name = "Foreign group" };
        await context
            .NestedSet<UserGroup>()
            .ForScope(firstTenant)
            .InsertRootAsync(firstGroup, Guid.NewGuid(), CancellationToken.None);

        await context
            .NestedSet<UserGroup>()
            .ForScope(secondTenant)
            .InsertRootAsync(foreignGroup, Guid.NewGuid(), CancellationToken.None);

        var user = new User
        {
            Name = "Misassigned user",
            Position = "Engineer",
        };

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

    /// <summary>The database FK rejects an out-of-tenant supervisor even when the hierarchy facade is bypassed.</summary>
    [Fact]
    public async Task ForeignTenantSupervisorFailsAtDatabaseBoundaryAsync()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var firstGroup = new UserGroup { Name = "First group" };
        var secondGroup = new UserGroup { Name = "Second group" };
        await context
            .NestedSet<UserGroup>()
            .ForScope(firstTenant)
            .InsertRootAsync(firstGroup, Guid.NewGuid(), CancellationToken.None);

        await context
            .NestedSet<UserGroup>()
            .ForScope(secondTenant)
            .InsertRootAsync(secondGroup, Guid.NewGuid(), CancellationToken.None);

        var firstRoot = new User
        {
            Name = "First",
            Position = "Director",
        };

        var foreignRoot = new User
        {
            Name = "Second",
            Position = "Director",
        };

        await context
            .NestedSet<User>()
            .ForScope(firstTenant)
            .InsertRootAsync(firstRoot, Guid.NewGuid(), CancellationToken.None);

        await context
            .NestedSet<User>()
            .ForScope(secondTenant)
            .InsertRootAsync(foreignRoot, Guid.NewGuid(), CancellationToken.None);

        // Act
        // WHY: This direct SQL update deliberately bypasses the facade to prove the relational tenant FK independently.
        var exception = await Record.ExceptionAsync(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Users\" SET \"SupervisorId\" = {foreignRoot.Id} WHERE \"Id\" = {firstRoot.Id}",
            CancellationToken.None));

        var persisted = await context
            .Users
            .AsNoTracking()
            .Where(user => user.Id == firstRoot.Id)
            .Select(user => new
            {
                user.SupervisorId,
                Left = EF.Property<long>(user, UserHierarchyProperties.Left),
                Right = EF.Property<long>(user, UserHierarchyProperties.Right),
            })
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.IsType<SqliteException>(exception, exactMatch: false);
        Assert.Null(persisted.SupervisorId);
        Assert.Equal((1L, 2L), (persisted.Left, persisted.Right));
    }

    /// <summary>Creates the sample context with the same registered nested-set extension used in production.</summary>
    private static UserGroupSqliteContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<UserGroupSqliteContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        return new UserGroupSqliteContext(options);
    }
}
