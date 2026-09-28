namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Verifies that a timed-out SQLite writer leaves hierarchy and tree-registry state intact.</summary>
public sealed class WriterTimeoutTests : ProviderTest, IClassFixture<ProviderFixture<ProviderResources, SqliteEngine>>
{
    /// <summary>Uses immutable SQLite ownership while each test owns its local resources.</summary>
    /// <param name="fixture">The provider fixture identifying this suite's SQLite engine.</param>
    public WriterTimeoutTests(
        ProviderFixture<ProviderResources, SqliteEngine> fixture
    ) : base(fixture) { }

    /// <summary>A blocked root insertion changes nothing and succeeds when retried after the writer releases its lock.</summary>
    [Fact]
    public async Task BusyWriterPreservesStructureAndRegistryUntilFreshRetry()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync(Engine);
        var firstTree = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondTree = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await using (var setup = database.CreateContext())
        {
            var hierarchy = setup
                .NestedSet<TreeNode>()
                .ForScope(7);
            await hierarchy.InsertRootAsync(new TreeNode { NodeId = 1 }, firstTree, CancellationToken.None);
        }

        var before = await SnapshotAsync(database);
        await using var writer = database.CreateContext();
        await using var transaction = await writer.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            CancellationToken.None);

        await using var contender = database.CreateContext();

        // WHY: SQLite obtains its single-writer lock at BEGIN IMMEDIATE. Setting the contender's connection
        // timeout isolates this probe from the fixture's longer default without delaying other SQLite tests.
        var contenderConnection = (Microsoft.Data.Sqlite.SqliteConnection)contender.Database.GetDbConnection();
        contenderConnection.DefaultTimeout = 1;
        var blockedTree = contender
            .NestedSet<TreeNode>()
            .ForScope(7);

        // Act
        var failure = await Record.ExceptionAsync(() => blockedTree.InsertRootAsync(
            new TreeNode { NodeId = 2 },
            secondTree,
            CancellationToken.None));

        await transaction.RollbackAsync(CancellationToken.None);
        var afterFailure = await SnapshotAsync(database);

        await using var retryContext = database.CreateContext();
        var retryTree = retryContext
            .NestedSet<TreeNode>()
            .ForScope(7);

        var retriedRoot = new TreeNode { NodeId = 2 };
        await retryTree.InsertRootAsync(retriedRoot, secondTree, CancellationToken.None);
        var afterRetry = await SnapshotAsync(database);

        // Assert
        var busy = Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(failure);
        Assert.Equal(5, busy.SqliteErrorCode);
        Assert.Equal(before.Nodes, afterFailure.Nodes);
        Assert.Equal(before.Registry, afterFailure.Registry);
        Assert.Equal(2, afterRetry.Nodes.Length);
        Assert.Equal(2, afterRetry.Registry.Length);
        Assert.Equal(7, retriedRoot.Tree);
        Assert.Equal(
            (1L, 2L, 0, (int?)null),
            (retriedRoot.Start, retriedRoot.End, retriedRoot.Depth, retriedRoot.Parent));
        Assert.Equal(secondTree, retriedRoot.TreeId);
        Assert.Equal(firstTree, afterRetry.Registry[0].TreeId);
        Assert.Equal(secondTree, afterRetry.Registry[1].TreeId);
        Assert.All(afterRetry.Registry, row => Assert.Equal(NestedSetTreeRegistryMetadata.Active, row.Lifecycle));
    }

    /// <summary>Reads persisted coordinates and typed registry identities through an independent context.</summary>
    private static async Task<SnapshotState> SnapshotAsync(
        TestDatabase database
    )
    {
        await using var context = database.CreateContext();
        var nodes = await context
            .Set<TreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.NodeId)
            .Select(node => new NodeState(
                node.NodeId,
                node.TreeId,
                node.Tree,
                node.Start,
                node.End,
                node.Depth,
                node.Position,
                node.Parent))
            .ToArrayAsync(CancellationToken.None);

        var hierarchy = context.Model.FindEntityType(typeof(TreeNode))!;
        var registry = Mapping.NestedSetTreeRegistryMapping.For(hierarchy).Registry;
        var rows = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .OrderBy(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId))
            .Select(row => new RegistryState(
                EF.Property<int>(row, NestedSetTreeRegistryMetadata.Scope),
                EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId),
                EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle)))
            .ToArrayAsync(CancellationToken.None);

        return new SnapshotState(nodes, rows);
    }

    /// <summary>Groups the two persisted state snapshots captured in one read phase.</summary>
    private sealed record SnapshotState(
        NodeState[] Nodes,
        RegistryState[] Registry
    );

    /// <summary>Stores each structural column by value for rollback comparison.</summary>
    private sealed record NodeState(
        int Id,
        Guid TreeId,
        int Scope,
        long Left,
        long Right,
        int Depth,
        long Position,
        int? Parent
    );

    /// <summary>Stores the typed identity and lifecycle of one tree-registry row.</summary>
    private sealed record RegistryState(
        int Scope,
        Guid TreeId,
        long Revision,
        byte Lifecycle
    );
}
