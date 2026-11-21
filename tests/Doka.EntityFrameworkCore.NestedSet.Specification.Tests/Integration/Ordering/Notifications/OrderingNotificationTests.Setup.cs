namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingNotificationTests
{
    /// <summary>Creates a context using the fixture's real provider and caller-owned observers.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The fixture database with independent notification tables.</param>
    /// <param name="interceptors">Observers registered only on the returned context.</param>
    /// <returns>A context disposed by its caller.</returns>
    private static async Task<OrderingNotificationContext> CreateContextAsync(
        TestDatabase database,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    ) => new(await SaveSemanticsFixture.OptionsAsync(database, cancellationToken: cancellationToken, interceptors));

    /// <summary>Seeds a movable subtree and unrelated persisted notification-tracked application data.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The database whose independent notification tables are created here.</param>
    /// <param name="firstUse">Whether the independent tables must be created before seeding.</param>
    /// <returns>A task that completes after the shared scenario is persisted.</returns>
    private static async Task SeedAsync(
        TestDatabase database,
        bool firstUse,
        CancellationToken cancellationToken
    )
    {
        await using var context = await CreateContextAsync(database, cancellationToken: cancellationToken);

        if (firstUse)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);
        }
        else
        {
            await context
                .Set<OrderingNotificationNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await context
                .Set<OrderingNotificationNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<OrderingNotificationPayload>()
                .ExecuteDeleteAsync(cancellationToken);
        }

        var tree = context
            .NestedSet<OrderingNotificationNode>()
            .ForScope(1);

        await tree.InsertRootAsync(
            new OrderingNotificationNode
            {
                Id = 1,
                Name = "Root",
            },
            Guid.NewGuid(),
            cancellationToken);

        await tree.InsertChildAsync(
            new OrderingNotificationNode
            {
                Id = 2,
                Name = "Alpha",
            },
            1,
            cancellationToken);

        await tree.InsertChildAsync(
            new OrderingNotificationNode
            {
                Id = 3,
                Name = "Bravo",
            },
            1,
            cancellationToken);

        await tree.InsertChildAsync(
            new OrderingNotificationNode
            {
                Id = 4,
                Name = "Leaf",
            },
            2,
            cancellationToken);

        await context.AddAsync(
            new OrderingNotificationPayload
            {
                Id = 1,
                Value = "original application value",
            },
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Loads every affected coordinate and creates two caller-owned notification changes.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="context">The context owning the hierarchy and unrelated pending payload.</param>
    /// <returns>The tracked nodes in stable identity order.</returns>
    private static async Task<OrderingNotificationNode[]> LoadAndRenameAsync(
        OrderingNotificationContext context,
        CancellationToken cancellationToken
    )
    {
        var nodes = await context
            .Set<OrderingNotificationNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(cancellationToken);

        var payload = await context
            .Set<OrderingNotificationPayload>()
            .SingleAsync(cancellationToken);

        nodes.Single(node => node.Id == 2)
            .Name = "Zulu";

        payload.Value = "pending application change";

        return nodes;
    }
}
