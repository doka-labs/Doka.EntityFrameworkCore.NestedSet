namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexLifecycleTests
{
    /// <summary>Refreshes generated values after a proven structural parent move.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task SaveChangesAsync_ParentSave_RefreshesFlagsAfterObservedStructuralWrite(
        string engine
    )
    {
        // Arrange
        var probe = new StructuralRefreshProbe(failRefresh: false);
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: options => options.AddInterceptors(probe),
            configureDesignServices: null);

        await using var context = await CreatePendingParentMoveAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        probe.Armed = true;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        probe.Armed = false;
        var persisted = await ReadLifecycleStatesAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var tracked = ReadTrackedLifecycleStates(context);
        var original = ReadOriginalLifecycleStates(context);

        // Assert
        AssertStructuralRefreshReached(probe);
        Assert.Equal(probe.DuringMove, persisted);
        Assert.Equal(persisted, tracked);
        Assert.Equal(persisted, original);
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
        Assert.All(
            context.ChangeTracker.Entries<MigrationNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    /// <summary>Restores complete tracked and persisted state after a proven late structural failure.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task SaveChangesAsync_LateStructuralFailure_RestoresGeneratedFlagsAndPendingParentEdit(
        string engine
    )
    {
        // Arrange
        var probe = new StructuralRefreshProbe(failRefresh: true);
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: options => options.AddInterceptors(probe),
            configureDesignServices: null);

        await using var context = await CreatePendingParentMoveAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var persistedBefore = await ReadLifecycleStatesAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var pendingBefore = ReadTrackedLifecycleStates(context);
        var originalBefore = ReadOriginalLifecycleStates(context);
        probe.Armed = true;

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        probe.Armed = false;
        var persisted = await ReadLifecycleStatesAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var tracked = ReadTrackedLifecycleStates(context);
        var original = ReadOriginalLifecycleStates(context);

        // Assert
        AssertStructuralRefreshReached(probe);
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(StructuralRefreshProbe.FailureMessage, failure.Message);
        Assert.Equal(persistedBefore, persisted);
        Assert.Equal(pendingBefore, tracked);
        Assert.Equal(originalBefore, original);
        Assert.Equal(
            EntityState.Modified,
            context
                .ChangeTracker
                .Entries<MigrationNode>()
                .Single(entry => entry.Entity.NodeId == 2)
                .State);
    }

    /// <summary>Persists a restored pending edit after an arranged structural failure.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task SaveChangesAsync_RestoredParentEdit_CanBeSavedAfterLateStructuralFailure(
        string engine
    )
    {
        // Arrange
        var probe = new StructuralRefreshProbe(failRefresh: true);
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: options => options.AddInterceptors(probe),
            configureDesignServices: null);

        await using var context = await CreatePendingParentMoveAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var persistedBefore = await ReadLifecycleStatesAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var pendingBefore = ReadTrackedLifecycleStates(context);
        var originalBefore = ReadOriginalLifecycleStates(context);
        probe.Armed = true;
        var priorFailure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        probe.Armed = false;
        var restoredDatabase = await ReadLifecycleStatesAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var restoredTracked = ReadTrackedLifecycleStates(context);
        var restoredOriginal = ReadOriginalLifecycleStates(context);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var persisted = await ReadLifecycleStatesAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var tracked = ReadTrackedLifecycleStates(context);
        var original = ReadOriginalLifecycleStates(context);

        // Assert
        AssertStructuralRefreshReached(probe);
        var failure = Assert.IsType<InvalidOperationException>(priorFailure);
        Assert.Equal(StructuralRefreshProbe.FailureMessage, failure.Message);
        Assert.Equal(persistedBefore, restoredDatabase);
        Assert.Equal(pendingBefore, restoredTracked);
        Assert.Equal(originalBefore, restoredOriginal);
        Assert.Equal(probe.DuringMove, persisted);
        Assert.Equal(persisted, tracked);
        Assert.Equal(persisted, original);
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
        Assert.All(
            context.ChangeTracker.Entries<MigrationNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    /// <summary>Creates the shared migrated topology and one detected pending cross-tree parent edit.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="database">The isolated database used by the scenario.</param>
    private static async Task<AncestorIndexContext> CreatePendingParentMoveAsync(
        AncestorIndexDatabase database,
        CancellationToken cancellationToken
    )
    {
        var chain = database.Scaffold(true, "AncestorLateStructuralRollback");
        await database.MigrateAsync(
            chain,
            cancellationToken: cancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        var context = database.CreateContext();

        try
        {
            await SeedAsync(context, cancellationToken: cancellationToken);
            await context
                .Set<MigrationNode>()
                .LoadAsync(cancellationToken);

            var moved = context
                .ChangeTracker
                .Entries<MigrationNode>()
                .Single(entry => entry.Entity.NodeId == 2)
                .Entity;

            moved.ParentId = 4;
            context.ChangeTracker.DetectChanges();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Proves a completed parent move was observed before the generated-value refresh.</summary>
    private static void AssertStructuralRefreshReached(
        StructuralRefreshProbe probe
    )
    {
        Assert.True(probe.StructuralWriteObserved);
        Assert.True(probe.GeneratedRefreshObserved);
        Assert.Equal(
            [2, 4],
            probe
                .DuringMove
                .Where(row => row.Flag)
                .Select(row => row.Id));

        Assert.Equal(
            4,
            Assert.Single(probe.DuringMove, row => row.Id == 2)
                .Parent);

        Assert.Equal(
            12,
            Assert.Single(probe.DuringMove, row => row.Id == 2)
                .Tree);
    }
}
