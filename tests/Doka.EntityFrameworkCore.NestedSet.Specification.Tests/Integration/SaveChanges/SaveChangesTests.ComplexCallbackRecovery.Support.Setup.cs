namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Seeds deterministic JSON through EF with empty materialization-time collection initializers.</summary>
    private async Task<TestDatabase> CreateComplexCallbackDatabaseAsync(
        CancellationToken cancellationToken
    )
    {
        var database = await TestDatabase.CreateAsync(Engine, cancellationToken: cancellationToken);

        try
        {
            await using var context = await CreateComplexCallbackContextAsync(
                database,
                cancellationToken: cancellationToken);

            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);

            var owner = new NavigationScopeOwner { Value = "owner" };
            var row = new ComplexCallbackRow
            {
                Id = 1,
                Items = [NewComplexCallbackItem("first"), NewComplexCallbackItem("second")],
                Address = new ComplexCallbackAddress
                {
                    Text = "original address",
                    Tags = NewComplexCallbackLeaves(),
                },
            };

            await context.AddRangeAsync(
                [
                    owner, new NavigationScopeOwner { Value = "other owner" },
                    new NavigationNote
                    {
                        Id = 1,
                        Value = "sentinel",
                    },
                    row,
                ],
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            var tree = context
                .NestedSet<NavigationNode>()
                .ForScope(owner.Id);

            await tree.InsertRootAsync(
                new NavigationNode
                {
                    Id = 1,
                    Name = "Root",
                },
                s_treeId,
                cancellationToken);

            await tree.InsertChildAsync(
                new NavigationNode
                {
                    Id = 2,
                    Name = "Alpha",
                },
                1,
                cancellationToken);

            await tree.InsertChildAsync(
                new NavigationNode
                {
                    Id = 3,
                    Name = "Bravo",
                },
                1,
                cancellationToken);

            return database;
        }
        catch
        {
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Creates the isolated ordinary-row model through the existing provider options.</summary>
    private static async Task<ComplexCallbackContext> CreateComplexCallbackContextAsync(
        TestDatabase database,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    )
        => new(await SaveSemanticsFixture.OptionsAsync(database, cancellationToken: cancellationToken, interceptors));

    /// <summary>Supplies explicit seed values without causing EF materialization to append duplicates.</summary>
    private static ComplexCallbackItem NewComplexCallbackItem(
        string text
    )
        => new()
        {
            Text = text,
            Parts = NewComplexCallbackLeaves(),
        };

    /// <summary>Provides two independent reference elements at every nested collection boundary.</summary>
    private static List<ComplexCallbackLeaf> NewComplexCallbackLeaves()
        => [new() { Text = "part one" }, new() { Text = "part two" }];

    /// <summary>Loads the ordinary row and sentinel without creating pending facade writes.</summary>
    private static async Task<ComplexCallbackGraph> LoadComplexCallbackGraphAsync(
        ComplexCallbackContext context,
        CancellationToken cancellationToken
    )
    {
        var row = await context
            .Set<ComplexCallbackRow>()
            .SingleAsync(cancellationToken);

        var sentinel = await context
            .Set<NavigationNote>()
            .SingleAsync(cancellationToken);

        context.ChangeTracker.AutoDetectChangesEnabled = false;

        return new ComplexCallbackGraph(context.Entry(row), context.Entry(sentinel));
    }

    /// <summary>Authorizes one existing hierarchy order change independently of ordinary complex payload.</summary>
    private static async Task PrepareComplexCallbackOrderingAsync(
        ComplexCallbackContext context,
        CancellationToken cancellationToken
    )
    {
        var node = await context
            .Set<NavigationNode>()
            .SingleAsync(value => value.Id == 2, cancellationToken);

        node.Name = "Zulu";
        context
            .Entry(node)
            .Property(value => value.Name)
            .IsModified = true;
    }
}
