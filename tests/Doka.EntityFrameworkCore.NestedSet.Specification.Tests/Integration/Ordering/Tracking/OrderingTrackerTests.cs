namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises ordinary EF tracker semantics across ordered save success, failure, and retry.</summary>
public abstract partial class OrderingTrackerTests : ProviderTest
{
    private readonly SaveSemanticsFixture _fixture;

    /// <summary>Creates a case using reusable engines with independently reset tracker tables.</summary>
    /// <param name="fixture">The databases owned by this test family.</param>
    protected OrderingTrackerTests(
        IProviderFixture<SaveSemanticsFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Prepares equivalent coordinated and ordinary EF reference rows on the requested provider.</summary>
    /// <param name="engine">The supported real relational provider.</param>
    private Task<TestDatabase> CreateDatabaseAsync(
        string engine
    ) => _fixture.PrepareAsync(engine, SeedDatabaseAsync);

    /// <summary>Creates or clears both models before seeding the shared tracker scenario.</summary>
    /// <param name="database">The fixture-owned database reused by serialized cases.</param>
    /// <param name="firstUse">Whether independent tracker tables must be created.</param>
    private static async Task SeedDatabaseAsync(
        TestDatabase database,
        bool firstUse
    )
    {
        var options = await OptionsAsync(database);
        await using var context = new TrackerContext(options);
        await using var plain = new PlainTrackerContext(options);

        if (firstUse)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);

            await plain
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);
        }
        else
        {
            await context
                .Set<TrackerChild>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await context
                .Set<TrackerOwner>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await context
                .Set<TrackerAddition>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await context
                .Set<TrackerNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);

            await context
                .Set<TrackerNode>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await plain
                .Set<TrackerNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);

            await plain
                .Set<TrackerNode>()
                .ExecuteDeleteAsync(CancellationToken.None);
        }

        var tree = Tree(context);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(Node(2, "Alpha"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(3, "Bravo"), 1, CancellationToken.None);
        await tree.InsertChildAsync(Node(4, "Charlie"), 1, CancellationToken.None);
        var owner = new TrackerOwner { Id = 1 };
        owner.Children.Add(new TrackerChild { Id = 1 });
        await context.AddAsync(owner, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var nodes = await tree
            .TreeContaining(1)
            .ToArrayAsync(CancellationToken.None);

        await plain.AddRangeAsync(nodes, CancellationToken.None);
        await plain.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Uses the established provider lifecycle with one payload command per observable batch.</summary>
    /// <param name="database">The fixture database with independent tracker tables.</param>
    /// <param name="interceptors">Observers registered only for this context.</param>
    private static Task<DbContextOptions> OptionsAsync(
        TestDatabase database,
        params IInterceptor[] interceptors
    ) => SaveSemanticsFixture.OptionsAsync(database, interceptors);

    /// <summary>Creates a detached payload for assignment of its scope, parent, and coordinates.</summary>
    private static TrackerNode Node(
        int id,
        string name
    ) => new() { Id = id, Name = name };

    /// <summary>Selects the public facade for the application partition used by this isolated database.</summary>
    private static ScopedNestedSet<TrackerNode, int> Tree(
        TrackerContext context
    ) => context
        .NestedSet<TrackerNode>()
        .ForScope(1);

    /// <summary>Reads a node through the ordinary tracking API, as application payload editing would do.</summary>
    private static Task<TrackerNode> LoadNodeAsync(
        DbContext context,
        int key
    ) => context
        .Set<TrackerNode>()
        .SingleAsync(node => node.Id == key, CancellationToken.None);

    /// <summary>Reads persisted sibling order without loading or mutating the tracked entities.</summary>
    private static Task<int[]> ChildIdsAsync(
        TrackerContext context
    ) => Tree(context)
        .ChildrenOf(1)
        .Select(node => node.Id)
        .ToArrayAsync(CancellationToken.None);

    /// <summary>
    ///     Captures visible tracker state without a fresh DetectChanges changing store-generated sidecars.
    /// </summary>
    private static EntrySnapshot Capture(
        DbContext context,
        object entity
    )
    {
        var automaticDetection = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            var entry = context.Entry(entity);

            return new EntrySnapshot(
                entry.State,
                entry
                    .Properties
                    .OrderBy(property => property.Metadata.Name, StringComparer.Ordinal)
                    .Select(property => new PropertySnapshot(
                        property.Metadata.Name,
                        property.CurrentValue,
                        property.OriginalValue,
                        property.IsModified,
                        property.IsTemporary))
                    .ToArray());
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        }
    }

    /// <summary>Checks both values and flags rather than treating EntityState alone as successful recovery.</summary>
    private static void AssertSnapshot(
        EntrySnapshot expected,
        EntrySnapshot actual
    )
    {
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Properties, actual.Properties);
    }

    /// <summary>Contains observable state for one pending or unchanged entity.</summary>
    /// <param name="State">The entity state before automatic change detection.</param>
    /// <param name="Properties">Current values, original values, and per-property flags.</param>
    private sealed record EntrySnapshot(
        EntityState State,
        PropertySnapshot[] Properties
    );

    /// <summary>Contains the public tracker contract for one scalar property.</summary>
    /// <param name="Name">The mapped property name.</param>
    /// <param name="Current">The current tracker value, including temporary or generated sidecars.</param>
    /// <param name="Original">The original value used for concurrency and change detection.</param>
    /// <param name="IsModified">Whether this scalar remains a pending write.</param>
    /// <param name="IsTemporary">Whether the value is temporary until a successful insert.</param>
    private sealed record PropertySnapshot(
        string Name,
        object? Current,
        object? Original,
        bool IsModified,
        bool IsTemporary
    );
}
