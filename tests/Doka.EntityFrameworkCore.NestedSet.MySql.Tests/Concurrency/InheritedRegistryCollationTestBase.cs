namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Verifies registry identity matches the inherited collation of its physical hierarchy table.</summary>
public abstract class InheritedRegistryCollationTestBase : ProviderTest
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
    public async Task InheritedBinaryScopesCanReserveTheSameTreeIdIndependently()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<InheritedBinaryScopeContext>(
            Engine, static options => new InheritedBinaryScopeContext(options));

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var firstScope = new BroadScope($"REGISTRY-INHERITED-{root}");
        var secondScope = new BroadScope(firstScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var firstRoot = new BroadScopeNode { Id = root, Name = "First root" };
        await context.NestedSet<BroadScopeNode>().ForScope(firstScope)
            .InsertRootAsync(firstRoot, treeId, CancellationToken.None);

        var before = (firstRoot.Scope.Value, firstRoot.Id, firstRoot.TreeId, firstRoot.ParentId,
            firstRoot.Left, firstRoot.Right, firstRoot.Depth, firstRoot.Position, firstRoot.Name);

        // WHY: Remove only arrangement-owned tracking so the test isolates registry reservation parity rather
        // than the independent explicit-mutation guard for tracked hierarchy rows.
        context.ChangeTracker.Clear();
        var secondRoot = new BroadScopeNode { Id = root + 10, Name = "Second root" };

        // Act
        var failure = await Record.ExceptionAsync(() => context.NestedSet<BroadScopeNode>().ForScope(secondScope)
            .InsertRootAsync(secondRoot, treeId, CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.True(firstScope.Equals(secondScope));
        var persistedFirst = await context.NestedSet<BroadScopeNode>().ForScope(firstScope).InTree(treeId).Nodes
            .AsNoTracking().SingleAsync(CancellationToken.None);

        var persistedSecond = await context.NestedSet<BroadScopeNode>().ForScope(secondScope).InTree(treeId).Nodes
            .AsNoTracking().SingleAsync(CancellationToken.None);

        Assert.Equal(before, (persistedFirst.Scope.Value, persistedFirst.Id,
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
    public async Task InheritedBinaryScopesAcquireBothLocksInCanonicalOrder(bool reverseRequests)
    {
        // Arrange
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync<InheritedBinaryScopeContext>(
            Engine, static options => new InheritedBinaryScopeContext(options), probe);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var firstScope = new BroadScope($"REGISTRY-ORDER-{root}");
        var secondScope = new BroadScope(firstScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var firstState = await SeedRootAsync(context, firstScope, treeId, root, "First root");
        var secondState = await SeedRootAsync(context, secondScope, treeId, root + 10, "Second root");
        BroadScope[] scopes = reverseRequests ? [secondScope, firstScope] : [firstScope, secondScope];
        var requests = Requests(context, scopes, treeId, NestedSetTreeLockMode.Existing);
        ClearCaptures(probe);

        // Act
        await AcquireAsync(context, requests);

        // Assert
        Assert.Equal(new[] { firstScope.Value, secondScope.Value }, LockScopes(probe));
        Assert.Equal(firstState, await ReadRootAsync(context, firstScope, treeId));
        Assert.Equal(secondState, await ReadRootAsync(context, secondScope, treeId));
        Assert.Equal(2, await RegistryCountAsync(context, treeId));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Explicit case-insensitive Scope comparison deduplicates locks despite a binary table default.</summary>
    [Fact]
    public async Task ExplicitScopeCollationDeduplicatesExistingAliasLocks()
    {
        // Arrange
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync<ExplicitRegistryScopeCollationContext>(
            Engine, static options => new ExplicitRegistryScopeCollationContext(options), probe);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var scope = new BroadScope($"REGISTRY-OVERRIDE-{root}");
        var alias = new BroadScope(scope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var before = await SeedRootAsync(context, scope, treeId, root, "Existing root");
        var requests = Requests(context, [scope, alias], treeId, NestedSetTreeLockMode.Existing);
        ClearCaptures(probe);

        // Act
        await AcquireAsync(context, requests);

        // Assert
        Assert.Equal(new[] { scope.Value }, LockScopes(probe));
        Assert.Equal(before, await ReadRootAsync(context, scope, treeId));
        Assert.Equal(before, await ReadRootAsync(context, alias, treeId));
        Assert.Equal(1, await RegistryCountAsync(context, treeId));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Equivalent Scope aliases cannot reserve a second root under the same TreeId.</summary>
    [Fact]
    public async Task ExplicitScopeCollationRejectsDuplicateTreeReservation()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<ExplicitRegistryScopeCollationContext>(
            Engine, static options => new ExplicitRegistryScopeCollationContext(options));

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var scope = new BroadScope($"REGISTRY-DUPLICATE-{root}");
        var alias = new BroadScope(scope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var before = await SeedRootAsync(context, scope, treeId, root, "Existing root");
        var rejected = new BroadScopeNode { Id = root + 10, Name = "Rejected root" };
        var rejectedBefore = Snapshot(rejected);

        // Act
        var failure = await Record.ExceptionAsync(() => context.NestedSet<BroadScopeNode>().ForScope(alias)
            .InsertRootAsync(rejected, treeId, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(rejectedBefore, Snapshot(rejected));
        Assert.Equal(EntityState.Detached, context.Entry(rejected).State);
        Assert.Equal(before, await ReadRootAsync(context, scope, treeId));
        Assert.Equal(before, await ReadRootAsync(context, alias, treeId));
        Assert.Equal(1, await RegistryCountAsync(context, treeId));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Native forest validation rejects equivalent Scope and TreeId aliases before any reservation.</summary>
    [Fact]
    public async Task ExplicitScopeCollationRejectsDuplicateImportedTreeIdentities()
    {
        // Arrange
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync<ExplicitRegistryScopeCollationContext>(
            Engine, static options => new ExplicitRegistryScopeCollationContext(options), probe);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var scope = new BroadScope($"REGISTRY-IMPORT-{root}");
        var alias = new BroadScope(scope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var requests = Requests(context, [scope, alias], treeId, NestedSetTreeLockMode.New);
        ClearCaptures(probe);

        // Act
        var failure = await Record.ExceptionAsync(() => NestedSetTreeLocks.RequireDistinctAsync(
            context, requests, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidImport, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Single(probe.Commands);
        Assert.Empty(LockScopes(probe));
        Assert.Empty(await context.NestedSet<BroadScopeNode>().ForScope(scope).InTree(treeId).Nodes
            .ToArrayAsync(CancellationToken.None));

        Assert.Equal(0, await RegistryCountAsync(context, treeId));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Seeds through the public mutation contract without retaining arrangement-owned tracked nodes.</summary>
    private static async Task<RootState> SeedRootAsync(
        BroadScopeContext context,
        BroadScope scope,
        Guid treeId,
        int key,
        string name
    )
    {
        var root = new BroadScopeNode { Id = key, Name = name };
        await context.NestedSet<BroadScopeNode>().ForScope(scope)
            .InsertRootAsync(root, treeId, CancellationToken.None);

        var state = Snapshot(root);

        // WHY: Registry-only acts must not be confounded by the independent explicit-mutation tracker guard.
        context.ChangeTracker.Clear();

        return state;
    }

    /// <summary>Creates complete typed requests for the production multi-tree locking regression seam.</summary>
    private static NestedSetTreeLockRequest<Guid, BroadScope>[] Requests(
        BroadScopeContext context,
        IReadOnlyList<BroadScope> scopes,
        Guid treeId,
        NestedSetTreeLockMode mode
    )
    {
        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;

        return scopes.Select(scope => new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType, scope, treeId, mode)).ToArray();
    }

    /// <summary>Exercises native deduplication and acquisition inside the normal transaction owner.</summary>
    private static Task AcquireAsync(
        BroadScopeContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests
    ) => NestedSetTransaction.ExecuteAsync(context,
        token => NestedSetTreeLocks.AcquireAsync(context, requests, token), CancellationToken.None);

    /// <summary>Discards arrangement-only observations without altering database or tracked state.</summary>
    private static void ClearCaptures(EnterpriseProbe probe)
    {
        probe.Commands.Clear();
        probe.ParameterValues.Clear();
        probe.ParameterNames.Clear();
    }

    /// <summary>Returns Scope bindings from actual registry lifecycle locks, excluding rowset parameters.</summary>
    private static string[] LockScopes(EnterpriseProbe probe)
        => probe.Commands.Zip(probe.ParameterValues)
            .Where(command => command.First.Contains(
                NestedSetTreeRegistryMetadata.Lifecycle, StringComparison.OrdinalIgnoreCase))
            .SelectMany(command => command.Second)
            .OfType<string>()
            .ToArray();

    /// <summary>Reads complete root structure through the public Scope and TreeId query contract.</summary>
    private static async Task<RootState> ReadRootAsync(BroadScopeContext context, BroadScope scope, Guid treeId)
    {
        var root = await context.NestedSet<BroadScopeNode>().ForScope(scope).InTree(treeId).Nodes
            .SingleAsync(CancellationToken.None);

        return Snapshot(root);
    }

    /// <summary>Counts only the test's fresh TreeId across the model's own physical registry.</summary>
    private static Task<int> RegistryCountAsync(BroadScopeContext context, Guid treeId)
    {
        var hierarchy = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy).Registry;

        return context.Set<NestedSetTreeRegistry>(registry.Name)
            .Where(row => EF.Property<Guid>(row, NestedSetTreeRegistryMetadata.TreeId) == treeId)
            .CountAsync(CancellationToken.None);
    }

    /// <summary>Preserves every hierarchy and payload value, including untouched detached input Scope.</summary>
    private static RootState Snapshot(BroadScopeNode root)
        // WHY: The facade assigns Scope during insertion; a rejected detached input must retain its original null.
        => new(root.Scope?.Value, root.Id, root.TreeId, root.ParentId,
            root.Left, root.Right, root.Depth, root.Position, root.Name);

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
