namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SqlServerAncestorCollectionTests
{
    /// <summary>An at-most-one principal lookup preserves both collection forms before and after its exact limit.</summary>
    /// <param name="indexed">Whether the real indexed navigation model is used.</param>
    /// <param name="dto">Whether collections are projected into arrays instead of materialized on entities.</param>
    /// <param name="ownerKey">The unique requested principal key.</param>
    /// <param name="hidden">Whether that principal is excluded by the model's query filter.</param>
    /// <param name="expectedCount">The independent zero-or-one expected principal count.</param>
    [Theory]
    [InlineData(false, false, 1, false, 1)]
    [InlineData(false, true, 1, false, 1)]
    [InlineData(true, false, 1, false, 1)]
    [InlineData(true, true, 1, false, 1)]
    [InlineData(false, false, 2, false, 1)]
    [InlineData(false, true, 2, false, 1)]
    [InlineData(true, false, 2, false, 1)]
    [InlineData(true, true, 2, false, 1)]
    [InlineData(false, false, 2, true, 0)]
    [InlineData(false, true, 2, true, 0)]
    [InlineData(true, false, 2, true, 0)]
    [InlineData(true, true, 2, true, 0)]
    [InlineData(false, false, 3, false, 0)]
    [InlineData(false, true, 3, false, 0)]
    [InlineData(true, false, 3, false, 0)]
    [InlineData(true, true, 3, false, 0)]
    [InlineData(false, false, 99, false, 0)]
    [InlineData(false, true, 99, false, 0)]
    [InlineData(true, false, 99, false, 0)]
    [InlineData(true, true, 99, false, 0)]
    public async Task UniquePrincipalLimitPreservesAllNavigationPayload(
        bool indexed, bool dto, int ownerKey, bool hidden, int expectedCount
    )
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var options = indexed
            ? (DbContextOptions)new DbContextOptionsBuilder<AncestorIndexQueryContext>()
                .UseSqlite(connection, provider => provider.UseNestedSets()).Options
            : new DbContextOptionsBuilder<CompositionNavigationContext>()
                .UseSqlite(connection, provider => provider.UseNestedSets()).Options;

        await using var services = NestedSetTestAdapters.BuildServices(options);
        await using (var schema = CreateContext(options, services, indexed))
        {
            await schema.Database.EnsureCreatedAsync(CancellationToken.None);
        }

        var probe = new CompositionCommandProbe();
        var observed = indexed
            ? (DbContextOptions)new DbContextOptionsBuilder<AncestorIndexQueryContext>(
                    (DbContextOptions<AncestorIndexQueryContext>)options).AddInterceptors(probe).Options
            : new DbContextOptionsBuilder<CompositionNavigationContext>(
                    (DbContextOptions<CompositionNavigationContext>)options).AddInterceptors(probe).Options;

        await using var context = CreateContext(observed, services, indexed);
        var nodes = Enumerable.Range(1, 3).Select(id => Node(id, "visible")).ToArray();
        nodes.Single(node => node.Id == 2).Visible = !hidden;
        await context.AddRangeAsync(nodes, CancellationToken.None);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        probe.Reset();

        // WHY: SQLite cannot execute APPLY. At the query root the same unique-key, native-tree and
        // actual containment lookup can execute its zero-or-one limit without changing adapter behavior.
        var pairs = context.NestedSet<CompositionNavigationNode>().ForScope(Guid.Empty).InTree(Guid.Empty).Nodes
            .Where(owner => owner.Id == ownerKey && owner.Left < 3 && owner.Right > 4 && 3 < owner.Right && 4 > owner.Left)
            .OrderByDescending(owner => owner.Left).Take(1)
            .Select(owner => new NavigationPair { Source = owner, Owner = owner, Distance = 2 - owner.Depth });

        // Act
        var actual = dto
            ? await CollectionDtos(pairs).AsSingleQuery().ToArrayAsync(CancellationToken.None)
            : (await pairs.AsSingleQuery().ToArrayAsync(CancellationToken.None)).Select(EntityDto).ToArray();

        // Assert
        Assert.Equal(expectedCount, actual.Length);
        Assert.All(actual, row =>
        {
            Assert.Equal(ownerKey, row.Owner);
            Assert.Equal(3 - ownerKey, row.Distance);
            Assert.Equal(new[] { $"tag-{ownerKey}-1", $"tag-{ownerKey}-2" }, row.OwnerTags);

            if (dto)
            {
                Assert.Equal(new[] { $"item-{ownerKey}-1", $"item-{ownerKey}-2" }, row.OwnerItems);
            }
        });

        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
