using Microsoft.EntityFrameworkCore.Migrations;
using App = Doka.EntityFrameworkCore.NestedSet.Samples.UserGroups;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class GroupSupervisorTests
{
    /// <summary>
    /// The executable provider lines apply the released initial and single additive supervisor migration.
    /// </summary>
    [EngineFact(ExcludedEngines = ["PostgreSql", "SqlServer"],
        Reason = "The console sample publishes Doka and SQLite migration lines; "
            + "PostgreSQL and SQL Server qualify the common supervisor model and queries.")]
    public async Task SampleMigrationChainCreatesTypedSupervisorSchema()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine, static _ => Task.CompletedTask);
        await using var source = database.CreateContext();
        await using var context = CreateUserGroupMigrationContext(source.Database.GetConnectionString()!);

        // Act
        await context.Database.MigrateAsync(CancellationToken.None);

        // Assert
        var applied = (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).ToArray();
        Assert.Equal(2, applied.Length);
        Assert.Equal(Engine == "Sqlite" ? "20260929182723_InitialUserGroups" : "20260929182713_InitialUserGroups",
            applied[0]);
        Assert.EndsWith("_GroupSupervisorQueries", applied[1], StringComparison.Ordinal);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(CancellationToken.None));
        Assert.Equal(0, await context.GroupTypes.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.MembershipRoles.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.UserRoles.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.GroupRelationPeriods.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.UserMetadataDefinitions.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.MembershipMetadataDefinitions.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.GroupMetadataDefinitions.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.UserMetadatas.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.MembershipMetadatas.CountAsync(CancellationToken.None));
        Assert.Equal(0, await context.GroupMetadatas.CountAsync(CancellationToken.None));
    }

    /// <summary>
    /// The additive migration preserves released user/group coordinates, memberships, roles, and effective privileges.
    /// </summary>
    [EngineFact(ExcludedEngines = ["PostgreSql", "SqlServer"],
        Reason = "The console sample publishes Doka and SQLite migration lines; "
            + "PostgreSQL and SQL Server have no executable sample migration set.")]
    public async Task SupervisorMigrationPreservesExistingApplicationDataAndGrants()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine, static _ => Task.CompletedTask);
        await using var source = database.CreateContext();
        await using var context = CreateUserGroupMigrationContext(source.Database.GetConnectionString()!);
        var initial = context.Database.GetMigrations().First();
        await context.GetService<IMigrator>().MigrateAsync(initial, CancellationToken.None);
        var tenant = Guid.NewGuid();
        var groupTree = Guid.NewGuid();
        var userTree = Guid.NewGuid();
        var role = Guid.NewGuid();
        var timestamp = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        var identifiers = context.GetService<ISqlGenerationHelper>();

        // WHY: The current entity model includes new columns absent from the released initial schema.
        // Seed that original schema directly; delimit only trusted identifiers and parameterize every stored value.
        var sql = $$"""
            INSERT INTO {{identifiers.DelimitIdentifier("UserGroups")}}
                ({{identifiers.DelimitIdentifier("Id")}}, {{identifiers.DelimitIdentifier("TenantId")}},
                 {{identifiers.DelimitIdentifier("TreeId")}}, {{identifiers.DelimitIdentifier("ParentId")}},
                 {{identifiers.DelimitIdentifier("Name")}}, {{identifiers.DelimitIdentifier("Left")}},
                 {{identifiers.DelimitIdentifier("Right")}}, {{identifiers.DelimitIdentifier("Depth")}},
                 {{identifiers.DelimitIdentifier("Position")}}, {{identifiers.DelimitIdentifier("UpdatedAtUtc")}})
                VALUES ({4}, {0}, {1}, NULL, {5}, {11}, {12}, {13}, {14}, {10});
            INSERT INTO {{identifiers.DelimitIdentifier("Users")}}
                ({{identifiers.DelimitIdentifier("Id")}}, {{identifiers.DelimitIdentifier("TenantId")}},
                 {{identifiers.DelimitIdentifier("SupervisorId")}}, {{identifiers.DelimitIdentifier("Name")}},
                 {{identifiers.DelimitIdentifier("Position")}}, {{identifiers.DelimitIdentifier("Depth")}},
                 {{identifiers.DelimitIdentifier("Left")}}, {{identifiers.DelimitIdentifier("Right")}},
                 {{identifiers.DelimitIdentifier("NestedSetPosition")}}, {{identifiers.DelimitIdentifier("TreeId")}})
                VALUES ({4}, {0}, NULL, {6}, {7}, {13}, {11}, {12}, {14}, {2});
            INSERT INTO {{identifiers.DelimitIdentifier("Roles")}}
                ({{identifiers.DelimitIdentifier("Id")}}, {{identifiers.DelimitIdentifier("TenantId")}},
                 {{identifiers.DelimitIdentifier("Name")}}, {{identifiers.DelimitIdentifier("UpdatedAtUtc")}})
                VALUES ({3}, {0}, {8}, {10});
            INSERT INTO {{identifiers.DelimitIdentifier("Privileges")}}
                ({{identifiers.DelimitIdentifier("TenantId")}}, {{identifiers.DelimitIdentifier("Code")}})
                VALUES ({0}, {9});
            INSERT INTO {{identifiers.DelimitIdentifier("UserGroupMemberships")}}
                ({{identifiers.DelimitIdentifier("TenantId")}}, {{identifiers.DelimitIdentifier("UserId")}},
                 {{identifiers.DelimitIdentifier("UserGroupId")}})
                VALUES ({0}, {4}, {4});
            INSERT INTO {{identifiers.DelimitIdentifier("UserGroupRoles")}}
                ({{identifiers.DelimitIdentifier("TenantId")}}, {{identifiers.DelimitIdentifier("UserGroupId")}},
                 {{identifiers.DelimitIdentifier("RoleId")}})
                VALUES ({0}, {4}, {3});
            INSERT INTO {{identifiers.DelimitIdentifier("RolePrivileges")}}
                ({{identifiers.DelimitIdentifier("TenantId")}}, {{identifiers.DelimitIdentifier("RoleId")}},
                 {{identifiers.DelimitIdentifier("PrivilegeCode")}})
                VALUES ({0}, {3}, {9});
            """;

        object[] values =
        [
            tenant, groupTree, userTree, role, 1, "Existing team", "Existing user", "Engineer", "Existing role",
            "directory.read", timestamp, 1L, 2L, 0, 0L,
        ];

        await context.Database.ExecuteSqlRawAsync(sql, values, CancellationToken.None);

        // Act
        await context.Database.MigrateAsync(CancellationToken.None);

        // Assert
        var group = await context.Groups.IgnoreQueryFilters().AsNoTracking().SingleAsync(CancellationToken.None);
        var user = await context.Users.IgnoreQueryFilters().AsNoTracking().Select(value => new
        {
            value.Id,
            value.TenantId,
            value.SupervisorId,
            value.Name,
            value.Position,
            value.Depth,
            value.Visible,
            TreeId = EF.Property<Guid>(value, App.UserHierarchyProperties.TreeId),
            Left = EF.Property<long>(value, App.UserHierarchyProperties.Left),
            Right = EF.Property<long>(value, App.UserHierarchyProperties.Right),
            NestedSetPosition = EF.Property<long>(value, App.UserHierarchyProperties.NestedSetPosition),
        }).SingleAsync(CancellationToken.None);

        var preservedRole = await context.Roles.IgnoreQueryFilters().AsNoTracking().SingleAsync(CancellationToken.None);
        var membership = await context.UserGroupMemberships.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var groupRole = await context.GroupRoles.AsNoTracking().SingleAsync(CancellationToken.None);
        var privilege = await context.Privileges.AsNoTracking().SingleAsync(CancellationToken.None);
        var grant = await context.RolePrivileges.AsNoTracking().SingleAsync(CancellationToken.None);
        var effective = await App.PrivilegeQueries.EffectiveForUserAsync(context, tenant, 1, CancellationToken.None);

        Assert.Equal(Engine == "Sqlite" ? "20260929182723_InitialUserGroups" : "20260929182713_InitialUserGroups",
            initial);
        Assert.Equal((1, tenant, groupTree), (group.Id, group.TenantId, group.TreeId));
        Assert.Equal("Existing team", group.Name);
        Assert.Equal((1L, 2L, 0, 0L), (group.Left, group.Right, group.Depth, group.Position));
        Assert.Null(group.ParentId);
        Assert.Equal(timestamp, group.UpdatedAtUtc);
        Assert.True(group.Visible);
        Assert.Null(group.TypeId);
        Assert.Equal((1, tenant, userTree), (user.Id, user.TenantId, user.TreeId));
        Assert.Equal("Existing user", user.Name);
        Assert.Equal("Engineer", user.Position);
        Assert.Equal((1L, 2L, 0, 0L), (user.Left, user.Right, user.Depth, user.NestedSetPosition));
        Assert.Null(user.SupervisorId);
        Assert.True(user.Visible);
        Assert.Equal((tenant, role, "Existing role", timestamp),
            (preservedRole.TenantId, preservedRole.Id, preservedRole.Name, preservedRole.UpdatedAtUtc));
        Assert.True(preservedRole.Visible);
        Assert.Equal((tenant, 1, 1), (membership.TenantId, membership.UserId, membership.UserGroupId));
        Assert.True(membership.Visible);
        Assert.Equal(0, membership.LeadershipPriority);
        Assert.Null(membership.FromDate);
        Assert.Null(membership.ToDate);
        Assert.Equal((tenant, 1, role), (groupRole.TenantId, groupRole.UserGroupId, groupRole.RoleId));
        Assert.Equal((tenant, "directory.read"), (privilege.TenantId, privilege.Code));
        Assert.Equal((tenant, role, "directory.read"), (grant.TenantId, grant.RoleId, grant.PrivilegeCode));
        Assert.Equal("directory.read", Assert.Single(effective));
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>
    /// The migrated schema supports the actual sample's role inserts and tenant-bound supervisor query.
    /// </summary>
    [EngineFact(ExcludedEngines = ["PostgreSql", "SqlServer"],
        Reason = "The console sample publishes Doka and SQLite migration lines; "
            + "PostgreSQL and SQL Server qualify the common supervisor model and queries.")]
    public async Task MigratedSupervisorSchemaRunsTheActualSampleScenario()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine, static _ => Task.CompletedTask);
        await using var source = database.CreateContext();
        await using var context = CreateUserGroupMigrationContext(source.Database.GetConnectionString()!);
        await context.Database.MigrateAsync(CancellationToken.None);
        var tenant = Guid.NewGuid();

        // Act
        var data = await App.SupervisorScenarioData.CreateAsync(context, tenant, CancellationToken.None);
        var result = await App.SupervisorQueries.ResolveAsync(context, tenant, data.Request(),
            App.SupervisorSelection.All, CancellationToken.None);

        // Assert
        Assert.Equal(App.SupervisorBatchStatus.Completed, result.Status);
        Assert.Equal(2, result.Origins.Count);
        Assert.All(result.Origins, origin =>
        {
            Assert.Equal(App.SupervisorResolutionStatus.Found, origin.Status);
            Assert.NotEmpty(origin.Evidence);
            Assert.All(origin.Evidence, row =>
            {
                Assert.Equal(tenant, row.TenantId);
                Assert.Equal(data.Users[0], row.SourceUserId);
                Assert.Equal(origin.StartGroupId, row.StartGroupId);
                Assert.Equal(data.TreeId, row.StartTreeId);
                Assert.NotEqual(data.Users[0], row.CandidateUserId);
            });
        });

        var main = Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[5]);
        Assert.Equal(data.Users.Skip(1).Take(2).Order(), main.Evidence.Select(row => row.CandidateUserId).Order());
        Assert.All(main.Evidence, row =>
        {
            Assert.Equal(data.Groups[4], row.FoundGroupId);
            Assert.Equal(1, row.Distance);
        });

        var independent = Assert.Single(result.Origins, origin => origin.StartGroupId == data.Groups[6]);
        var inherited = Assert.Single(independent.Evidence);
        Assert.Equal(data.Users[5], inherited.CandidateUserId);
        Assert.Equal(data.Groups[0], inherited.FoundGroupId);
        Assert.Equal(2, inherited.Distance);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Selects the executable sample context with the provider's actual checked-in migration set.</summary>
    /// <param name="connection">The fresh database connection owned by this test.</param>
    /// <returns>The Doka or SQLite context; neither creates a schema as a construction side effect.</returns>
    private App.UserGroupContext CreateUserGroupMigrationContext(string connection) => Engine == "Sqlite"
        ? new App.UserGroupSqliteContext(
            ModelCompatibilityDatabase.Options<App.UserGroupSqliteContext>(Engine, connection))
        : new App.UserGroupContext(ModelCompatibilityDatabase.Options<App.UserGroupContext>(Engine, connection));
}
