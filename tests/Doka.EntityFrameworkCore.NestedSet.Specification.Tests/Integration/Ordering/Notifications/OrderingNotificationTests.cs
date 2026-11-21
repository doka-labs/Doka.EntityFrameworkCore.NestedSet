namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks ordered saves and recovery when EF deliberately omits ordinary scalar original values.</summary>
public abstract partial class OrderingNotificationTests : ProviderTest
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
    public async Task SaveChangesAsync_RenameWithoutScalarOriginals_RefreshesTrackedStructure(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        var database = await _fixture.PrepareAsync(
            Engine,
            SeedAsync,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        await LoadAndRenameAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var saved = await context.SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None);

        // Assert
        Assert.Equal(2, saved);
        await AssertSuccessfulSaveAsync(
            database,
            context,
            acceptAllChangesOnSuccess,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Verifies a late failure restores refreshed coordinates and unrelated pending payload.</summary>
    /// <param name="acceptAllChangesOnSuccess">The requested acceptance mode of the failing save.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChangesAsync_LateFailure_RestoresNotificationStateWithoutScalarOriginals(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        var database = await _fixture.PrepareAsync(
            Engine,
            SeedAsync,
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = new CommitFailureProbe();
        await using var context = await CreateContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        var nodes = await LoadAndRenameAsync(context, cancellationToken: TestContext.Current.CancellationToken);
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
        Assert.Equal(
            EntityState.Modified,
            context.Entry(renamed)
                .State);

        Assert.True(
            context
                .Entry(renamed)
                .Property(node => node.Name)
                .IsModified);

        Assert.Equal(
            EntityState.Modified,
            context.Entry(payload)
                .State);

        Assert.True(
            context
                .Entry(payload)
                .Property(row => row.Value)
                .IsModified);

        Assert.Equal(
            5,
            context
                .ChangeTracker
                .Entries()
                .Count());

        Assert.Null(context.Database.CurrentTransaction);
        AssertUnavailableOriginals(context, nodes, payload);
        await using var verification = await CreateContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var persisted = await verification
            .Set<OrderingNotificationNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(before, Coordinates(persisted));
        Assert.Equal(
            "Alpha",
            persisted.Single(node => node.Id == 2)
                .Name);

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
    public async Task SaveChangesAsync_RetryAfterLateFailure_PersistsTheRetainedNotificationChanges(
        bool acceptAllChangesOnSuccess
    )
    {
        // Arrange
        var database = await _fixture.PrepareAsync(
            Engine,
            SeedAsync,
            cancellationToken: TestContext.Current.CancellationToken);

        var failure = new CommitFailureProbe();
        await using var context = await CreateContextAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken,
            failure);

        await LoadAndRenameAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var initialFailure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        failure.FailOnCommit = false;

        // Act
        var saved = await context.SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None);

        // Assert
        Assert.IsType<OrderingInjectedException>(initialFailure);
        Assert.Equal(2, failure.CommitAttempts);
        Assert.Equal(4, failure.RenamedLeftAtCommit);
        Assert.Equal(2, saved);
        await AssertSuccessfulSaveAsync(
            database,
            context,
            acceptAllChangesOnSuccess,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Copies structural values into immutable tuples without retaining mutable entity references.</summary>
    /// <param name="nodes">The nodes in stable identity order.</param>
    /// <returns>The complete coordinates compared before and after transaction recovery.</returns>
    private static (int Id, long Left, long Right, int Depth, long Position)[] Coordinates(
        IEnumerable<OrderingNotificationNode> nodes
    ) => nodes
        .Select(node => (node.Id, node.Left, node.Right, node.Depth, node.Position))
        .ToArray();
}
