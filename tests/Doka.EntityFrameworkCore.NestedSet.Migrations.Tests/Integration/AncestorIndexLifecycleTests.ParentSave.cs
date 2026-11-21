namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

public sealed partial class AncestorIndexLifecycleTests
{
    /// <summary>Refreshes tracked shadow flags during coordinated same-tree and cross-tree saves.</summary>
    /// <param name="engine">The actual relational provider.</param>
    /// <param name="crossTree">Whether the parent change crosses a stable tree boundary.</param>
    [Theory]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    [InlineData("MySql", false)]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("MariaDb", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("SqlServer", true)]
    public async Task SaveChangesAsync_ParentSave_RefreshesAllAffectedTrackedGeneratedFlags(
        string engine,
        bool crossTree
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null);

        var chain = database.Scaffold(true, "AncestorTrackedSave");
        await database.MigrateAsync(
            chain,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
        await using var context = database.CreateContext();
        await SeedAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var rows = await context
            .Set<MigrationNode>()
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var moved = rows.Single(x => x.NodeId
            == (crossTree
                ? 2
                : 3));

        moved.ParentId = crossTree
            ? 4
            : 1;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var persisted = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var tracked = ReadTrackedStates(context);
        int[] expectedBranches = crossTree
            ? [2, 4]
            : [1];

        // Assert
        Assert.Equal(persisted, tracked);
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
        Assert.Equal(
            expectedBranches,
            persisted
                .Where(x => x.Flag)
                .Select(x => x.Id));

        Assert.All(
            context.ChangeTracker.Entries<MigrationNode>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    /// <summary>Restores tracked generated values when a parent save fails after payload persistence.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task SaveChangesAsync_FailedParentSave_RestoresTrackedGeneratedFlags(
        string engine
    )
    {
        // Arrange
        var failure = new LateSaveFailure();
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: options => options.AddInterceptors(failure),
            configureDesignServices: null);

        var chain = database.Scaffold(true, "AncestorParentRollback");
        await database.MigrateAsync(
            chain,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false,
            cancellationToken: TestContext.Current.CancellationToken);
        await using var context = database.CreateContext();
        await SeedAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var rows = await context
            .Set<MigrationNode>()
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var before = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var moved = rows.Single(x => x.NodeId == 2);
        moved.ParentId = 4;
        failure.Armed = true;

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var persisted = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var tracked = ReadTrackedStates(context);

        // Assert
        var actual = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(LateSaveFailure.Message, actual.Message);
        Assert.Equal(before, persisted);
        Assert.Equal(before, tracked);
        Assert.Equal(4, moved.ParentId);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(moved)
                .State);
    }
}
