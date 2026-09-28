namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks ordered saves and recovery when EF deliberately omits ordinary scalar original values.</summary>
public abstract class OrderingNotificationTests : ProviderTest
{
    private readonly SaveSemanticsFixture _fixture;

    /// <summary>Creates a case using reusable provider lifetimes and independently reset notification tables.</summary>
    /// <param name="fixture">The databases owned by this test family.</param>
    protected OrderingNotificationTests(
        IProviderFixture<SaveSemanticsFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Verifies notification-driven renames refresh structure and respect both acceptance modes.</summary>
    /// <param name="acceptAllChangesOnSuccess">Whether the caller wants EF to accept the completed save.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenameWithoutScalarOriginalsRefreshesTrackedStructure(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        var database = await _fixture.PrepareAsync(Engine, SeedAsync);
        await using var context = await CreateContextAsync(database);
        await LoadAndRenameAsync(context);

        // Act
        var saved = await context.SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None);

        // Assert
        Assert.Equal(2, saved);
        await AssertSuccessfulSaveAsync(database, context, acceptAllChangesOnSuccess);
    }

    /// <summary>Verifies a late failure restores refreshed coordinates and unrelated pending payload.</summary>
    /// <param name="acceptAllChangesOnSuccess">The requested acceptance mode of the failing save.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LateFailureRestoresNotificationStateWithoutScalarOriginals(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        var database = await _fixture.PrepareAsync(Engine, SeedAsync);
        var failure = new CommitFailureProbe();
        await using var context = await CreateContextAsync(database, failure);
        var nodes = await LoadAndRenameAsync(context);
        var before = Coordinates(nodes);
        var renamed = nodes.Single(node => node.Id == 2);
        var payload = context
            .Set<OrderingNotificationPayload>()
            .Local
            .Single();

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            CancellationToken.None));

        // Assert
        Assert.IsType<OrderingInjectedException>(exception);
        Assert.Equal(1, failure.CommitAttempts);
        Assert.Equal(4, failure.RenamedLeftAtCommit);
        Assert.Equal(before, Coordinates(nodes));
        Assert.Equal("Zulu", renamed.Name);
        Assert.Equal("pending application change", payload.Value);
        Assert.Equal(EntityState.Modified, context.Entry(renamed).State);
        Assert.True(
            context
                .Entry(renamed)
                .Property(node => node.Name)
                .IsModified);
        Assert.Equal(EntityState.Modified, context.Entry(payload).State);
        Assert.True(
            context
                .Entry(payload)
                .Property(row => row.Value)
                .IsModified);
        Assert.Equal(5, context.ChangeTracker.Entries().Count());
        Assert.Null(context.Database.CurrentTransaction);
        AssertUnavailableOriginals(context, nodes, payload);
        await using var verification = await CreateContextAsync(database);
        var persisted = await verification
            .Set<OrderingNotificationNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(before, Coordinates(persisted));
        Assert.Equal(
            "Alpha",
            persisted.Single(node => node.Id == 2).Name);
        Assert.Equal(
            "original application value",
            await verification
                .Set<OrderingNotificationPayload>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
    }

    /// <summary>Verifies the same notifying entities remain usable after a late failed save is rolled back.</summary>
    /// <param name="acceptAllChangesOnSuccess">The requested acceptance mode of the retry.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetryAfterLateFailurePersistsTheRetainedNotificationChanges(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        var database = await _fixture.PrepareAsync(Engine, SeedAsync);
        var failure = new CommitFailureProbe();
        await using var context = await CreateContextAsync(database, failure);
        await LoadAndRenameAsync(context);
        var initialFailure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        failure.FailOnCommit = false;

        // Act
        var saved = await context.SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None);

        // Assert
        Assert.IsType<OrderingInjectedException>(initialFailure);
        Assert.Equal(2, failure.CommitAttempts);
        Assert.Equal(4, failure.RenamedLeftAtCommit);
        Assert.Equal(2, saved);
        await AssertSuccessfulSaveAsync(database, context, acceptAllChangesOnSuccess);
    }

    /// <summary>Creates a context using the fixture's real provider and caller-owned observers.</summary>
    /// <param name="database">The fixture database with independent notification tables.</param>
    /// <param name="interceptors">Observers registered only on the returned context.</param>
    /// <returns>A context disposed by its caller.</returns>
    private static async Task<OrderingNotificationContext> CreateContextAsync(
        TestDatabase database,
        params IInterceptor[] interceptors
    ) => new(await SaveSemanticsFixture.OptionsAsync(database, interceptors));

    /// <summary>Seeds a movable subtree and unrelated persisted notification-tracked application data.</summary>
    /// <param name="database">The database whose independent notification tables are created here.</param>
    /// <param name="firstUse">Whether the independent tables must be created before seeding.</param>
    /// <returns>A task that completes after the shared scenario is persisted.</returns>
    private static async Task SeedAsync(
        TestDatabase database,
        bool firstUse
    )
    {
        await using var context = await CreateContextAsync(database);

        if (firstUse)
        {
            await context
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(CancellationToken.None);
        }
        else
        {
            await context
                .Set<OrderingNotificationNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);

            await context
                .Set<OrderingNotificationNode>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await context
                .Set<OrderingNotificationPayload>()
                .ExecuteDeleteAsync(CancellationToken.None);
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
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNotificationNode
            {
                Id = 2,
                Name = "Alpha",
            },
            1,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNotificationNode
            {
                Id = 3,
                Name = "Bravo",
            },
            1,
            CancellationToken.None);

        await tree.InsertChildAsync(
            new OrderingNotificationNode
            {
                Id = 4,
                Name = "Leaf",
            },
            2,
            CancellationToken.None);

        await context.AddAsync(
            new OrderingNotificationPayload
            {
                Id = 1,
                Value = "original application value"
            },
            CancellationToken.None);

        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Loads every affected coordinate and creates two caller-owned notification changes.</summary>
    /// <param name="context">The context owning the hierarchy and unrelated pending payload.</param>
    /// <returns>The tracked nodes in stable identity order.</returns>
    private static async Task<OrderingNotificationNode[]> LoadAndRenameAsync(
        OrderingNotificationContext context
    )
    {
        var nodes = await context
            .Set<OrderingNotificationNode>()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var payload = await context
            .Set<OrderingNotificationPayload>()
            .SingleAsync(CancellationToken.None);

        nodes.Single(node => node.Id == 2).Name = "Zulu";
        payload.Value = "pending application change";

        return nodes;
    }

    /// <summary>Compares tracked coordinates with an independent database read and verifies pending flags.</summary>
    /// <param name="database">The database read through an independent verification context.</param>
    /// <param name="context">The context whose caller-owned state must match the acceptance mode.</param>
    /// <param name="accepted">Whether the successful save accepted the caller's pending changes.</param>
    /// <returns>A task that completes after all persisted and tracked assertions.</returns>
    private static async Task AssertSuccessfulSaveAsync(
        TestDatabase database,
        OrderingNotificationContext context,
        bool accepted
    )
    {
        var nodes = context
            .Set<OrderingNotificationNode>()
            .Local
            .OrderBy(node => node.Id)
            .ToArray();

        var payload = context
            .Set<OrderingNotificationPayload>()
            .Local
            .Single();

        var renamed = nodes.Single(node => node.Id == 2);
        await using var verification = await CreateContextAsync(database);
        var persisted = await verification
            .Set<OrderingNotificationNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(
            new[]
            {
                (1, 1L, 8L, 0, 0L),
                (2, 4L, 7L, 1, 1L),
                (3, 2L, 3L, 1, 0L),
                (4, 5L, 6L, 2, 0L),
            },
            Coordinates(persisted));
        Assert.Equal(Coordinates(persisted), Coordinates(nodes));
        Assert.Equal("Zulu", persisted.Single(node => node.Id == 2).Name);
        Assert.Equal(
            accepted ? EntityState.Unchanged : EntityState.Modified,
            context.Entry(renamed).State);
        Assert.Equal(
            !accepted,
            context
                .Entry(renamed)
                .Property(node => node.Name)
                .IsModified);
        Assert.Equal(
            accepted ? EntityState.Unchanged : EntityState.Modified,
            context.Entry(payload).State);
        Assert.Equal(
            !accepted,
            context
                .Entry(payload)
                .Property(row => row.Value)
                .IsModified);
        Assert.All(
            nodes.Where(node => node.Id != 2),
            node => Assert.Equal(
                EntityState.Unchanged,
                context.Entry(node).State));
        Assert.Equal("pending application change", payload.Value);
        Assert.Equal(
            "pending application change",
            await verification
                .Set<OrderingNotificationPayload>()
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
        AssertUnavailableOriginals(context, nodes, payload);
        var tree = verification
            .NestedSet<OrderingNotificationNode>()
            .ForScope(1);
        var report = await tree
            .InTree(persisted[0].TreeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        Assert.True(report.IsValid);
    }

    /// <summary>Proves the scenario has no original-value storage and leaves managed coordinates unmodified.</summary>
    /// <param name="context">The context whose actual public entry behavior is inspected.</param>
    /// <param name="nodes">The tracked hierarchy nodes.</param>
    /// <param name="payload">The unrelated notification-tracked application entity.</param>
    private static void AssertUnavailableOriginals(
        OrderingNotificationContext context,
        IEnumerable<OrderingNotificationNode> nodes,
        OrderingNotificationPayload payload
    )
    {
        foreach (var node in nodes)
        {
            var entry = context.Entry(node);
            foreach (var name in new[] { "Left", "Right", "Depth", "Position" })
            {
                // WHY: This negative control fails if the model accidentally falls back to snapshot tracking.
                Assert.Throws<InvalidOperationException>(() => entry.Property(name).OriginalValue);
                Assert.False(entry.Property(name).IsModified);
            }

            Assert.Throws<InvalidOperationException>(() => entry.Property(value => value.Name).OriginalValue);
        }

        Assert.Throws<InvalidOperationException>(() => context
            .Entry(payload)
            .Property(row => row.Value)
            .OriginalValue);
    }

    /// <summary>Copies structural values into immutable tuples without retaining mutable entity references.</summary>
    /// <param name="nodes">The nodes in stable identity order.</param>
    /// <returns>The complete coordinates compared before and after transaction recovery.</returns>
    private static (int Id, long Left, long Right, int Depth, long Position)[] Coordinates(
        IEnumerable<OrderingNotificationNode> nodes
    ) => nodes
        .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position))
        .ToArray();

    /// <summary>Fails after payload writes, hierarchy repair, and tracked refresh but before database commit.</summary>
    private sealed class CommitFailureProbe : DbTransactionInterceptor
    {
        /// <summary>Gets or sets whether the next commit attempt fails.</summary>
        internal bool FailOnCommit { get; set; } = true;

        /// <summary>Gets the number of save boundaries that reached the actual commit attempt.</summary>
        internal int CommitAttempts { get; private set; }

        /// <summary>Gets the refreshed coordinate visible immediately before the commit attempt.</summary>
        internal long RenamedLeftAtCommit { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            var context = eventData.Context
                ?? throw new InvalidOperationException("The observed transaction has no owning context.");

            CommitAttempts++;
            RenamedLeftAtCommit = context
                .ChangeTracker
                .Entries<OrderingNotificationNode>()
                .Single(entry => entry.Entity.Id == 2)
                .Entity
                .Left;

            if (FailOnCommit)
            {
                // WHY: Throwing here exercises rollback after both database and tracked coordinates were refreshed.
                throw new OrderingInjectedException();
            }

            return ValueTask.FromResult(result);
        }
    }
}
