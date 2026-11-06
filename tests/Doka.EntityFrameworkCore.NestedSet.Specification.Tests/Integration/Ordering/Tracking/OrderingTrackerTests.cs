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
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="engine">The supported real relational provider.</param>
    private Task<TestDatabase> CreateDatabaseAsync(
        string engine,
        CancellationToken cancellationToken
    ) => _fixture.PrepareAsync(engine, SeedDatabaseAsync, cancellationToken: cancellationToken);

    /// <summary>Creates or clears both models before seeding the shared tracker scenario.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The fixture-owned database reused by serialized cases.</param>
    /// <param name="firstUse">Whether independent tracker tables must be created.</param>
    private static async Task SeedDatabaseAsync(
        TestDatabase database,
        bool firstUse,
        CancellationToken cancellationToken
    )
    {
        var options = await OptionsAsync(database, cancellationToken: cancellationToken);
        await using var context = new TrackerContext(options);
        await using var plain = new PlainTrackerContext(options);

        if (firstUse)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);

            await plain
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(cancellationToken);
        }
        else
        {
            await context
                .Set<TrackerChild>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<TrackerOwner>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<TrackerAddition>()
                .ExecuteDeleteAsync(cancellationToken);

            await context
                .Set<TrackerNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await context
                .Set<TrackerNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await plain
                .Set<TrackerNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    cancellationToken);

            await plain
                .Set<TrackerNode>()
                .ExecuteDeleteAsync(cancellationToken);
        }

        var tree = Tree(context);
        await tree.InsertRootAsync(Node(1, "Root"), Guid.NewGuid(), cancellationToken);
        await tree.InsertChildAsync(Node(2, "Alpha"), 1, cancellationToken);
        await tree.InsertChildAsync(Node(3, "Bravo"), 1, cancellationToken);
        await tree.InsertChildAsync(Node(4, "Charlie"), 1, cancellationToken);
        var owner = new TrackerOwner { Id = 1 };
        owner.Children.Add(new TrackerChild { Id = 1 });
        await context.AddAsync(owner, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        var nodes = await tree
            .TreeContaining(1)
            .ToArrayAsync(cancellationToken);

        await plain.AddRangeAsync(nodes, cancellationToken);
        await plain.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Uses the established provider lifecycle with one payload command per observable batch.</summary>
    /// <param name="cancellationToken">The caller's token for setup, queries and verification.</param>
    /// <param name="database">The fixture database with independent tracker tables.</param>
    /// <param name="interceptors">Observers registered only for this context.</param>
    private static Task<DbContextOptions> OptionsAsync(
        TestDatabase database,
        CancellationToken cancellationToken,
        params IInterceptor[] interceptors
    ) => SaveSemanticsFixture.OptionsAsync(database, cancellationToken: cancellationToken, interceptors);

    /// <summary>Creates a detached payload for assignment of its scope, parent, and coordinates.</summary>
    private static TrackerNode Node(
        int id,
        string name
    ) => new()
    {
        Id = id,
        Name = name,
    };

    /// <summary>Selects the public facade for the application partition used by this isolated database.</summary>
    private static ScopedNestedSet<TrackerNode, int> Tree(
        TrackerContext context
    ) => context
        .NestedSet<TrackerNode>()
        .ForScope(1);

    /// <summary>Reads a node through the ordinary tracking API, as application payload editing would do.</summary>
    private static Task<TrackerNode> LoadNodeAsync(
        DbContext context,
        int key,
        CancellationToken cancellationToken
    ) => context
        .Set<TrackerNode>()
        .SingleAsync(node => node.Id == key, cancellationToken);

    /// <summary>Reads persisted sibling order without loading or mutating the tracked entities.</summary>
    private static Task<int[]> ChildIdsAsync(
        TrackerContext context,
        CancellationToken cancellationToken
    ) => Tree(context)
        .ChildrenOf(1)
        .Select(node => node.Id)
        .ToArrayAsync(cancellationToken);

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
