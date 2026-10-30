using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Verifies generated shadow flags follow committed and rolled-back structural writes.</summary>
[DatabasePlatform]
[Collection(AncestorIndexFixtureRegistration.Name)]
public sealed partial class AncestorIndexLifecycleTests
{
    private readonly MigrationFixture _fixture;

    /// <summary>Creates mutation scenarios on an independently migrated physical schema.</summary>
    /// <param name="fixture">The provider engine owner.</param>
    public AncestorIndexLifecycleTests(
        MigrationFixture fixture
    )
    {
        _fixture = fixture;
    }

    /// <summary>Recomputes database-generated shadow values for every explicit structural mutation family.</summary>
    /// <param name="engine">The actual relational provider.</param>
    /// <param name="operation">The independently verified mutation family.</param>
    [Theory]
    [InlineData("Sqlite", "insert")]
    [InlineData("Sqlite", "move")]
    [InlineData("Sqlite", "cross-tree")]
    [InlineData("Sqlite", "promote")]
    [InlineData("Sqlite", "subtree")]
    [InlineData("Sqlite", "bulk")]
    [InlineData("MySql", "insert")]
    [InlineData("MySql", "move")]
    [InlineData("MySql", "cross-tree")]
    [InlineData("MySql", "promote")]
    [InlineData("MySql", "subtree")]
    [InlineData("MySql", "bulk")]
    [InlineData("MariaDb", "insert")]
    [InlineData("MariaDb", "move")]
    [InlineData("MariaDb", "cross-tree")]
    [InlineData("MariaDb", "promote")]
    [InlineData("MariaDb", "subtree")]
    [InlineData("MariaDb", "bulk")]
    [InlineData("PostgreSql", "insert")]
    [InlineData("PostgreSql", "move")]
    [InlineData("PostgreSql", "cross-tree")]
    [InlineData("PostgreSql", "promote")]
    [InlineData("PostgreSql", "subtree")]
    [InlineData("PostgreSql", "bulk")]
    [InlineData("SqlServer", "insert")]
    [InlineData("SqlServer", "move")]
    [InlineData("SqlServer", "cross-tree")]
    [InlineData("SqlServer", "promote")]
    [InlineData("SqlServer", "subtree")]
    [InlineData("SqlServer", "bulk")]
    public async Task StructuralMutationRecomputesGeneratedShadowFlag(
        string engine,
        string operation
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(_fixture, engine);
        var chain = database.Scaffold(true, "AncestorLifecycle");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext();
        await SeedAsync(context);
        var hierarchy = context
            .NestedSet<MigrationNode>()
            .ForScope(7);

        // Act
        switch (operation)
        {
            case "insert":
                await hierarchy.InsertChildAsync(new MigrationNode { NodeId = 5 }, 4, CancellationToken.None);
                break;
            case "move":
                await hierarchy.MoveToAsync(3, 1, CancellationToken.None);
                break;
            case "cross-tree":
                await hierarchy.MoveToAsync(2, 4, CancellationToken.None);
                break;
            case "promote":
                await hierarchy.DeleteAsync(2, CancellationToken.None);
                break;
            case "subtree":
                await hierarchy.DeleteSubtreeAsync(2, CancellationToken.None);
                break;
            case "bulk":
                await hierarchy.InsertSubtreeAsync(
                    new NestedSetBranch<MigrationNode>(
                        new MigrationNode { NodeId = 5 },
                        [new NestedSetBranch<MigrationNode>(new MigrationNode { NodeId = 6 })]),
                    4,
                    CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        var persisted = await ReadStatesAsync(context);
        var first = await hierarchy
            .InTree(11)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        var second = await hierarchy
            .InTree(12)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
        Assert.Empty(context.ChangeTracker.Entries<MigrationNode>());
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.DoesNotContain(typeof(MigrationNode).GetProperties(), x => x.Name == AncestorIndexContext.FlagProperty);

        var expectedBranches = operation switch
        {
            "insert" => new[] { 1, 2, 4 },
            "move" => [1],
            "cross-tree" => [2, 4],
            "promote" => [1],
            "subtree" => [],
            "bulk" => [1, 2, 4, 5],
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Assert.Equal(expectedBranches, persisted.Where(x => x.Flag).Select(x => x.Id));
    }

    /// <summary>Rebuild recomputes a deliberately stale generated flag through the existing refresh boundary.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task RebuildRefreshesGeneratedFlagAfterCoordinateRepair(
        string engine
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(_fixture, engine);
        var chain = database.Scaffold(true, "AncestorRebuild");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext();
        await SeedAsync(context);
        var root = await context
            .Set<MigrationNode>()
            .SingleAsync(x => x.NodeId == 1, CancellationToken.None);

        await context
            .Set<MigrationNode>()
            .Where(x => x.NodeId == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Right, 2), CancellationToken.None);

        await context
            .Entry(root)
            .ReloadAsync(CancellationToken.None);

        var before = context
            .Entry(root)
            .Property<bool>(AncestorIndexContext.FlagProperty)
            .CurrentValue;

        // WHY: Explicit structural operations require affected hierarchy rows to be untracked.
        context.ChangeTracker.Clear();

        // Act
        await context
            .NestedSet<MigrationNode>()
            .ForScope(7)
            .InTree(11)
            .RebuildAsync(CancellationToken.None);

        var persisted = await ReadStatesAsync(context);

        // Assert
        Assert.False(before);
        Assert.Empty(context.ChangeTracker.Entries<MigrationNode>());
        Assert.True(Assert.Single(persisted, x => x.Id == 1).Flag);
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
    }

    /// <summary>Late payload failure restores generated flags and coordinates before the caller retries.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task FailedInsertionRestoresTrackedAndPersistedFlags(
        string engine
    )
    {
        // Arrange
        var failure = new LateSaveFailure();
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            options => options.AddInterceptors(failure));

        var chain = database.Scaffold(true, "AncestorRollback");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext();
        await SeedAsync(context);
        var before = await ReadStatesAsync(context);
        failure.Armed = true;

        // Act
        var exception = await Record.ExceptionAsync(() => context
            .NestedSet<MigrationNode>()
            .ForScope(7)
            .InsertChildAsync(new MigrationNode { NodeId = 5 }, 4, CancellationToken.None));

        var persisted = await ReadStatesAsync(context);
        var tracked = ReadTrackedStates(context);

        // Assert
        var actual = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(LateSaveFailure.Message, actual.Message);
        Assert.Equal(before, persisted);
        Assert.Empty(tracked);
    }

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
    public async Task ParentSaveRefreshesAllAffectedTrackedGeneratedFlags(
        string engine,
        bool crossTree
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(_fixture, engine);
        var chain = database.Scaffold(true, "AncestorTrackedSave");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext();
        await SeedAsync(context);
        var rows = await context
            .Set<MigrationNode>()
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var moved = rows.Single(x => x.NodeId == (crossTree ? 2 : 3));
        moved.ParentId = crossTree ? 4 : 1;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        var persisted = await ReadStatesAsync(context);
        var tracked = ReadTrackedStates(context);
        int[] expectedBranches = crossTree ? [2, 4] : [1];

        // Assert
        Assert.Equal(persisted, tracked);
        Assert.All(persisted, row => Assert.Equal(row.Right > row.Left + 1, row.Flag));
        Assert.Equal(expectedBranches, persisted.Where(x => x.Flag).Select(x => x.Id));
        Assert.All(context.ChangeTracker.Entries<MigrationNode>(),
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
    public async Task FailedParentSaveRestoresTrackedGeneratedFlags(
        string engine
    )
    {
        // Arrange
        var failure = new LateSaveFailure();
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            options => options.AddInterceptors(failure));

        var chain = database.Scaffold(true, "AncestorParentRollback");
        await database.MigrateAsync(chain);
        await using var context = database.CreateContext();
        await SeedAsync(context);
        var rows = await context
            .Set<MigrationNode>()
            .OrderBy(x => x.NodeId)
            .ToArrayAsync(CancellationToken.None);

        var before = await ReadStatesAsync(context);
        var moved = rows.Single(x => x.NodeId == 2);
        moved.ParentId = 4;
        failure.Armed = true;

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var persisted = await ReadStatesAsync(context);
        var tracked = ReadTrackedStates(context);

        // Assert
        var actual = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(LateSaveFailure.Message, actual.Message);
        Assert.Equal(before, persisted);
        Assert.Equal(before, tracked);
        Assert.Equal(4, moved.ParentId);
        Assert.Equal(EntityState.Modified, context.Entry(moved).State);
    }

    /// <summary>Evaluates the generated predicate safely at the largest representable valid Int64 bounds.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task GeneratedFlagHandlesLargestValidInt64LeafAndBranchBounds(
        string engine
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(_fixture, engine);
        var chain = database.Scaffold(true, "AncestorInt64Edge");
        await database.MigrateAsync(chain);
        var leaf = new MigrationNode
        {
            NodeId = 61,
            Scope = 7,
            TreeId = 11,
            Left = long.MaxValue - 1,
            Right = long.MaxValue,
        };

        var branch = new MigrationNode
        {
            NodeId = 62,
            Scope = 7,
            TreeId = 12,
            Left = long.MaxValue - 3,
            Right = long.MaxValue,
        };

        // Act
        await database.PhysicalDatabase.InsertNodeDirectlyAsync(leaf);
        await database.PhysicalDatabase.InsertNodeDirectlyAsync(branch);
        await using var context = database.CreateContext();
        var rows = await ReadStatesAsync(context);

        // Assert
        Assert.Equal([false, true], rows.Select(x => x.Flag));
        Assert.All(rows, row => Assert.Equal(long.MaxValue, row.Right));
    }

    /// <summary>Creates two tracked trees, including one leaf root and a three-level branch.</summary>
    private static async Task SeedAsync(
        AncestorIndexContext context
    )
    {
        var hierarchy = context
            .NestedSet<MigrationNode>()
            .ForScope(7);

        await hierarchy.InsertRootAsync(new MigrationNode { NodeId = 1 }, 11, CancellationToken.None);
        await hierarchy.InsertChildAsync(new MigrationNode { NodeId = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new MigrationNode { NodeId = 3 }, 2, CancellationToken.None);
        await hierarchy.InsertRootAsync(new MigrationNode { NodeId = 4 }, 12, CancellationToken.None);
    }

    /// <summary>Reads database values without identity resolution hiding stale tracker values.</summary>
    private static Task<FlagState[]> ReadStatesAsync(
        AncestorIndexContext context
    ) => context
        .Set<MigrationNode>()
        .AsNoTracking()
        .OrderBy(x => x.NodeId)
        .Select(x => new FlagState(x.NodeId, x.Left, x.Right, EF.Property<bool>(x, AncestorIndexContext.FlagProperty)))
        .ToArrayAsync(CancellationToken.None);

    /// <summary>Captures generated shadow sidecars without any database I/O.</summary>
    private static FlagState[] ReadTrackedStates(
        AncestorIndexContext context
    ) => context
        .ChangeTracker
        .Entries<MigrationNode>()
        .Where(x => x.State is not EntityState.Detached and not EntityState.Deleted)
        .Select(x => new FlagState(
            x.Entity.NodeId,
            x.Entity.Left,
            x.Entity.Right,
            x.Property<bool>(AncestorIndexContext.FlagProperty)
                .CurrentValue))
        .OrderBy(x => x.Id)
        .ToArray();

    /// <summary>A detached comparison of persisted or tracked bounds and the generated shadow value.</summary>
    /// <param name="Id">The application-assigned node key.</param>
    /// <param name="Left">The current left boundary.</param>
    /// <param name="Right">The current right boundary.</param>
    /// <param name="Flag">The database-derived branch marker.</param>
    private sealed record FlagState(
        int Id,
        long Left,
        long Right,
        bool Flag
    );

    /// <summary>Fails after the payload has been persisted inside the library-owned transaction boundary.</summary>
    private sealed class LateSaveFailure : SaveChangesInterceptor
    {
        /// <summary>The exact deliberate failure used to distinguish rollback from an unrelated exception.</summary>
        internal const string Message = "Deliberate ancestor-index payload failure.";

        /// <summary>Gets or sets whether the next payload-save completion must fail.</summary>
        internal bool Armed { get; set; }

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        ) => Armed ? throw new InvalidOperationException(Message) : ValueTask.FromResult(result);
    }
}
