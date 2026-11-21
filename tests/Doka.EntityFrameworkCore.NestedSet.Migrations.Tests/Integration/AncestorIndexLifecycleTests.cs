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
    public async Task InsertChildAsync_StructuralMutation_RecomputesGeneratedShadowFlag(
        string engine,
        string operation
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null);

        var chain = database.Scaffold(true, "AncestorLifecycle");
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

        var persisted = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);
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

        Assert.Equal(
            expectedBranches,
            persisted
                .Where(x => x.Flag)
                .Select(x => x.Id));
    }

    /// <summary>Rebuild recomputes a deliberately stale generated flag through the existing refresh boundary.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task RebuildAsync_Rebuild_RefreshesGeneratedFlagAfterCoordinateRepair(
        string engine
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null);

        var chain = database.Scaffold(true, "AncestorRebuild");
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

        var persisted = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(before);
        Assert.Empty(context.ChangeTracker.Entries<MigrationNode>());
        Assert.True(
            Assert.Single(persisted, x => x.Id == 1)
                .Flag);

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
    public async Task InsertChildAsync_FailedInsertion_RestoresTrackedAndPersistedFlags(
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

        var chain = database.Scaffold(true, "AncestorRollback");
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
        var before = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        failure.Armed = true;

        // Act
        var exception = await Record.ExceptionAsync(() => context
            .NestedSet<MigrationNode>()
            .ForScope(7)
            .InsertChildAsync(new MigrationNode { NodeId = 5 }, 4, CancellationToken.None));

        var persisted = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var tracked = ReadTrackedStates(context);

        // Assert
        var actual = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(LateSaveFailure.Message, actual.Message);
        Assert.Equal(before, persisted);
        Assert.Empty(tracked);
    }

    /// <summary>Evaluates the generated predicate safely at the largest representable valid Int64 bounds.</summary>
    /// <param name="engine">The actual relational provider.</param>
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public async Task InsertNodeDirectlyAsync_GeneratedFlag_HandlesLargestValidInt64LeafAndBranchBounds(
        string engine
    )
    {
        // Arrange
        await using var database = await AncestorIndexDatabase.CreateAsync(
            _fixture,
            engine,
            cancellationToken: TestContext.Current.CancellationToken,
            configureOptions: null,
            configureDesignServices: null);

        var chain = database.Scaffold(true, "AncestorInt64Edge");
        await database.MigrateAsync(
            chain,
            cancellationToken: TestContext.Current.CancellationToken,
            enabled: true,
            target: null,
            adoptIndex: false,
            enableIntegration: true,
            splitStructure: false);
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
        await database.PhysicalDatabase.InsertNodeDirectlyAsync(
            leaf,
            cancellationToken: TestContext.Current.CancellationToken);

        await database.PhysicalDatabase.InsertNodeDirectlyAsync(
            branch,
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = database.CreateContext();
        var rows = await ReadStatesAsync(context, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([false, true], rows.Select(x => x.Flag));
        Assert.All(rows, row => Assert.Equal(long.MaxValue, row.Right));
    }
}
