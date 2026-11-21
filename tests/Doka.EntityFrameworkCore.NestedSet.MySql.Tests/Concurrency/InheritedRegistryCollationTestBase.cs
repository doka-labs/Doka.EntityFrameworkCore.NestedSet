namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Verifies registry identity matches the inherited collation of its physical hierarchy table.</summary>
public abstract partial class InheritedRegistryCollationTestBase : ProviderTest
{
    // WHY: The two concrete engine suites retain one family-wide allocator when cases run concurrently.
    private static int s_nextRoot = 4000000;
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses isolated provider databases with the existing inherited binary Scope model.</summary>
    /// <param name="fixture">The shared provider fixture with independently owned model tables.</param>
    protected InheritedRegistryCollationTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Database-distinct Scopes can independently reserve the same TreeId with local root bounds.</summary>
    [Fact]
    public async Task InsertRootAsync_InheritedBinaryScopes_CanReserveTheSameTreeIdIndependently()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<InheritedBinaryScopeContext>(
            Engine,
            static options => new InheritedBinaryScopeContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var firstScope = new BroadScope($"REGISTRY-INHERITED-{root}");
        var secondScope = new BroadScope(firstScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var firstRoot = new BroadScopeNode
        {
            Id = root,
            Name = "First root",
        };

        await context
            .NestedSet<BroadScopeNode>()
            .ForScope(firstScope)
            .InsertRootAsync(firstRoot, treeId, CancellationToken.None);

        var before = (firstRoot.Scope.Value, firstRoot.Id, firstRoot.TreeId, firstRoot.ParentId,
            firstRoot.Left, firstRoot.Right, firstRoot.Depth, firstRoot.Position, firstRoot.Name);

        // WHY: Remove only arrangement-owned tracking so the test isolates registry reservation parity rather
        // than the independent explicit-mutation guard for tracked hierarchy rows.
        context.ChangeTracker.Clear();
        var secondRoot = new BroadScopeNode
        {
            Id = root + 10,
            Name = "Second root",
        };

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadScopeNode>()
            .ForScope(secondScope)
            .InsertRootAsync(secondRoot, treeId, CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.True(firstScope.Equals(secondScope));
        var persistedFirst = await context
            .NestedSet<BroadScopeNode>()
            .ForScope(firstScope)
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var persistedSecond = await context
            .NestedSet<BroadScopeNode>()
            .ForScope(secondScope)
            .InTree(treeId)
            .Nodes
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(
            before,
            (persistedFirst.Scope.Value, persistedFirst.Id,
                persistedFirst.TreeId, persistedFirst.ParentId, persistedFirst.Left, persistedFirst.Right,
                persistedFirst.Depth, persistedFirst.Position, persistedFirst.Name));

        Assert.Equal(firstScope.Value, persistedFirst.Scope.Value);
        Assert.Equal(secondScope.Value, persistedSecond.Scope.Value);
        Assert.Equal(root, persistedFirst.Id);
        Assert.Equal(root + 10, persistedSecond.Id);
        Assert.Equal(treeId, persistedFirst.TreeId);
        Assert.Equal(treeId, persistedSecond.TreeId);
        Assert.Equal(1L, persistedFirst.Left);
        Assert.Equal(2L, persistedFirst.Right);
        Assert.Equal(1L, persistedSecond.Left);
        Assert.Equal(2L, persistedSecond.Right);
        Assert.Equal(0, persistedFirst.Depth);
        Assert.Equal(0, persistedSecond.Depth);
        Assert.Equal(0L, persistedFirst.Position);
        Assert.Equal(0L, persistedSecond.Position);
        Assert.Null(persistedFirst.ParentId);
        Assert.Null(persistedSecond.ParentId);
    }

    /// <summary>Binary Scope identities retain both registry locks in the same order for either input order.</summary>
    /// <param name="reverseRequests">Whether requests arrive in the opposite order to binary storage ordering.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcquireAsync_InheritedBinaryScopes_AcquireBothLocksInCanonicalOrder(
        bool reverseRequests
    )
    {
        // Arrange
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync<InheritedBinaryScopeContext>(
            Engine,
            static options => new InheritedBinaryScopeContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var firstScope = new BroadScope($"REGISTRY-ORDER-{root}");
        var secondScope = new BroadScope(firstScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var firstState = await SeedRootAsync(
            context,
            firstScope,
            treeId,
            root,
            "First root",
            cancellationToken: TestContext.Current.CancellationToken);

        var secondState = await SeedRootAsync(
            context,
            secondScope,
            treeId,
            root + 10,
            "Second root",
            cancellationToken: TestContext.Current.CancellationToken);

        BroadScope[] scopes = reverseRequests
            ? [secondScope, firstScope]
            : [firstScope, secondScope];

        var requests = Requests(context, scopes, treeId, NestedSetTreeLockMode.Existing);
        ClearCaptures(probe);

        // Act
        await AcquireAsync(context, requests, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new[] { firstScope.Value, secondScope.Value }, LockScopes(probe));
        Assert.Equal(
            firstState,
            await ReadRootAsync(
                context,
                firstScope,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            secondState,
            await ReadRootAsync(
                context,
                secondScope,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            2,
            await RegistryCountAsync(
                context,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Seeds through the public mutation contract without retaining arrangement-owned tracked nodes.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="scope">The hierarchy scope used by the scenario.</param>
    /// <param name="treeId">The hierarchy tree identity.</param>
    /// <param name="key">The synthetic primary key.</param>
    /// <param name="name">The synthetic object or scenario name.</param>
    private static async Task<RootState> SeedRootAsync(
        BroadScopeContext context,
        BroadScope scope,
        Guid treeId,
        int key,
        string name,
        CancellationToken cancellationToken
    )
    {
        var root = new BroadScopeNode
        {
            Id = key,
            Name = name,
        };

        await context
            .NestedSet<BroadScopeNode>()
            .ForScope(scope)
            .InsertRootAsync(root, treeId, cancellationToken);

        var state = Snapshot(root);

        // WHY: Registry-only acts must not be confounded by the independent explicit-mutation tracker guard.
        context.ChangeTracker.Clear();

        return state;
    }

    /// <summary>Reads complete root structure through the public Scope and TreeId query contract.</summary>
    /// <param name="cancellationToken">Cancels this operation without interrupting resource cleanup.</param>
    /// <param name="context">The context used for provider operations.</param>
    /// <param name="scope">The hierarchy scope used by the scenario.</param>
    /// <param name="treeId">The hierarchy tree identity.</param>
    private static async Task<RootState> ReadRootAsync(
        BroadScopeContext context,
        BroadScope scope,
        Guid treeId,
        CancellationToken cancellationToken
    )
    {
        var root = await context
            .NestedSet<BroadScopeNode>()
            .ForScope(scope)
            .InTree(treeId)
            .Nodes
            .SingleAsync(cancellationToken);

        return Snapshot(root);
    }

    /// <summary>Preserves every hierarchy and payload value, including untouched detached input Scope.</summary>
    private static RootState Snapshot(
            BroadScopeNode root
        )

        // WHY: The facade assigns Scope during insertion; a rejected detached input must retain its original null.
        => new(
            root.Scope?.Value,
            root.Id,
            root.TreeId,
            root.ParentId,
            root.Left,
            root.Right,
            root.Depth,
            root.Position,
            root.Name);

    /// <summary>Captures exact root values independently of broad domain Scope equality.</summary>
    /// <param name="Scope">The exact stored Scope text, or null on an untouched detached input.</param>
    /// <param name="Id">The assigned node key.</param>
    /// <param name="TreeId">The stable tree identity.</param>
    /// <param name="ParentId">The raw direct-parent key.</param>
    /// <param name="Left">The inclusive left boundary.</param>
    /// <param name="Right">The inclusive right boundary.</param>
    /// <param name="Depth">The ancestor count.</param>
    /// <param name="Position">The sibling position.</param>
    /// <param name="Name">The unchanged payload value.</param>
    private readonly record struct RootState(
        string? Scope,
        int Id,
        Guid TreeId,
        int? ParentId,
        long Left,
        long Right,
        int Depth,
        long Position,
        string Name
    );
}
