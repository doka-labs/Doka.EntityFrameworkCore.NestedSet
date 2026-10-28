namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Distinguishes nullable missing group ordinals from real zero and negative group keys.</summary>
public sealed class SupervisorGroupOrdinalKeyTests
{
    /// <summary>Both group selection modes preserve real keys without interpreting them as missing slots.</summary>
    /// <param name="rootKey">A valid nonpositive group key.</param>
    /// <param name="typeFirst">Whether type selection precedes person qualification.</param>
    /// <param name="number">The one-based group ordinal.</param>
    /// <param name="found">Whether that ordinal exists on the path.</param>
    [Theory]
    [InlineData(0, true, 2, true)]
    [InlineData(0, true, 3, false)]
    [InlineData(0, false, 2, true)]
    [InlineData(0, false, 3, false)]
    [InlineData(-1, true, 2, true)]
    [InlineData(-1, true, 3, false)]
    [InlineData(-1, false, 2, true)]
    [InlineData(-1, false, 3, false)]
    public async Task GroupOrdinalAbsenceDoesNotReserveARealGroupKey(
        int rootKey,
        bool typeFirst,
        int number,
        bool found
    )
    {
        // Arrange
        await using var context = new UserGroupContext(new DbContextOptionsBuilder<UserGroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets()).Options);

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        var tenant = Guid.NewGuid();
        var tree = Guid.NewGuid();
        context.GroupTypes.AddRange(new GroupType { TenantId = tenant, Id = 1, Name = "Managers" },
            new GroupType { TenantId = tenant, Id = 2, Name = "Cluster" });
        await context.SaveChangesAsync(CancellationToken.None);
        var source = new User { Name = "Source", Position = "Contributor" };
        var manager = new User { Name = "Manager", Position = "Lead" };
        await context.NestedSet<User>().ForScope(tenant)
            .InsertRootAsync(source, Guid.NewGuid(), CancellationToken.None);
        await context.NestedSet<User>().ForScope(tenant)
            .InsertRootAsync(manager, Guid.NewGuid(), CancellationToken.None);

        // WHY: Raw parameterized fixture inserts preserve key zero, which EF's generated identity sentinel
        // would otherwise replace, including its dependent membership key. The complete valid parent chain
        // is unrelated to identity generation.
        await InsertGroupAsync(context, tenant, tree, rootKey, null, 1, 6, 0, 1);
        await InsertGroupAsync(context, tenant, tree, 10, rootKey, 2, 5, 1, 1);
        await InsertGroupAsync(context, tenant, tree, 20, 10, 3, 4, 2, 2);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "UserGroupMemberships" ("TenantId", "UserId", "UserGroupId", "LeadershipPriority", "Visible")
            VALUES ({0}, {1}, 20, 0, 1), ({0}, {2}, 10, 0, 1), ({0}, {2}, {3}, 0, 1)
            """, [tenant, source.Id, manager.Id, rootKey], CancellationToken.None);
        context.ChangeTracker.Clear();
        var request = new SupervisorRequest
        {
            SourceUserId = source.Id, StartGroupId = 20, SearchStart = SearchStart.ParentsOnly,
            Roles = new NoRoleFilter(), Matches = [],
            Groups = typeFirst ? new NthTypeGroupThenValidate(1, number) : new NthEligibleGroup([], number),
        };

        // Act
        var result = await SupervisorQueries.ResolveAsync(
            context, tenant, request, SupervisorSelection.RequireUnique, CancellationToken.None);

        // Assert
        Assert.Equal(SupervisorBatchStatus.Completed, result.Status);
        var origin = Assert.Single(result.Origins);
        Assert.Equal(found ? SupervisorResolutionStatus.Found : SupervisorResolutionStatus.NotFound, origin.Status);
        Assert.Equal(found ? manager.Id : (int?)null, origin.Continuation?.CandidateUserId);
        Assert.Equal(found ? rootKey : (int?)null, origin.Continuation?.FoundGroupId);
        Assert.Equal(found ? 1 : 0, origin.Evidence.Count);
        Assert.All(origin.Evidence, row =>
        {
            Assert.Equal(rootKey, row.FoundGroupId);
            Assert.Equal(2, row.Distance);
            Assert.Equal(tree, row.StartTreeId);
        });
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Inserts one trusted SQLite fixture row with parameterized scalar values.</summary>
    private static Task<int> InsertGroupAsync(
        UserGroupContext context,
        Guid tenant,
        Guid tree,
        int key,
        int? parent,
        long left,
        long right,
        int depth,
        int type
    ) => context.Database.ExecuteSqlRawAsync(
        """
        INSERT INTO "UserGroups" ("Id", "TenantId", "TreeId", "ParentId", "Name", "Left", "Right", "Depth",
            "Position", "UpdatedAtUtc", "TypeId", "Visible")
        VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, 0, {8}, {9}, 1)
        """,
        [key, tenant, tree, new SqliteParameter { DbType = DbType.Int32, Value = (object?)parent ?? DBNull.Value },
            $"Group {key}", left, right, depth, DateTime.UnixEpoch, type],
        CancellationToken.None);
}
