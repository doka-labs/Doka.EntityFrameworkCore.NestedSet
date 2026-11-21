namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that explicit mutation guards use complete native database identities.</summary>
[Collection("Model compatibility")]
public abstract partial class NativeTrackedIdentityGuardTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses existing isolated provider databases and model-compatible native collations.</summary>
    protected NativeTrackedIdentityGuardTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Rejects an affected tracked Scope alias even when CLR scope comparison differs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteTreeAsync_NativeScopeAlias_CannotLeaveAffectedTrackedStructureStale(
        bool scopedKey
    )
    {
        // Arrange
        await using DbContext context = scopedKey
            ? await _fixture.CreateContextAsync<ScopedScopeAliasContext>(
                Engine,
                static options => new ScopedScopeAliasContext(options),
                cancellationToken: TestContext.Current.CancellationToken)
            : await _fixture.CreateContextAsync<ScalarScopeAliasContext>(
                Engine,
                static options => new ScalarScopeAliasContext(options),
                cancellationToken: TestContext.Current.CancellationToken);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var scope = $"guard-scope-{root}";
        var treeId = Guid.NewGuid();
        var hierarchy = context
            .NestedSet<ScopeAliasNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(
            new ScopeAliasNode
            {
                Id = root,
                Name = "Root",
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ScopeAliasNode
            {
                Id = root + 1,
                Name = "Child",
            },
            root,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var persisted = await context
            .Set<ScopeAliasNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Id == root + 1 && node.Scope == scope, CancellationToken.None);

        var alias = persisted.WithScope(scope.ToUpperInvariant());
        context.Attach(alias);
        var before = (alias.TreeId, alias.Left, alias.Right, alias.Depth, alias.Position, alias.ParentId);

        // Act
        var failure = await Record.ExceptionAsync(() => hierarchy.DeleteTreeAsync(treeId, CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(alias)
                .State);

        Assert.Equal(before, (alias.TreeId, alias.Left, alias.Right, alias.Depth, alias.Position, alias.ParentId));
        Assert.Equal(
            2,
            await hierarchy
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Rejects a converted TreeId alias using its native case-insensitive comparison.</summary>
    [Fact]
    public async Task DeleteTreeAsync_NativeTreeAlias_CannotLeaveAffectedTrackedStructureStale()
    {
        // Arrange
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = $"guard-tree-{root}";
        await SeedTreeAsync(context, treeId, root, 1, cancellationToken: TestContext.Current.CancellationToken);
        var alias = await ReadNodeAsync(context, root + 1, cancellationToken: TestContext.Current.CancellationToken);
        alias.TreeId = new BroadTreeId(treeId.ToUpperInvariant());
        context.Attach(alias);
        var before = (alias.Left, alias.Right, alias.Depth, alias.Position, alias.ParentId);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(treeId), CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(alias)
                .State);

        Assert.Equal(before, (alias.Left, alias.Right, alias.Depth, alias.Position, alias.ParentId));
        Assert.Equal(
            2,
            await context
                .Set<BroadTreeIdNode>()
                .AsNoTracking()
                .CountAsync(node => node.Id >= root && node.Id <= root + 1, CancellationToken.None));
    }

    /// <summary>A broad model comparer cannot reject a tracked row in a distinct stored tree.</summary>
    [Fact]
    public async Task DeleteTreeAsync_BroadModelComparer_AllowsAnUnaffectedProviderDistinctTree()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadGuardTreeContext>(
            Engine,
            static options => new BroadGuardTreeContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var first = $"GUARD-{root}";
        var second = first.ToLowerInvariant();
        await SeedTreeAsync(context, first, root, 1, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeAsync(context, second, root + 10, 1, cancellationToken: TestContext.Current.CancellationToken);
        var tracked = await ReadNodeAsync(context, root + 11, cancellationToken: TestContext.Current.CancellationToken);
        context.Attach(tracked);
        var before = (tracked.TreeId.Value, tracked.Left, tracked.Right, tracked.Depth, tracked.Position);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(first), CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(tracked)
                .State);

        Assert.Equal(before, (tracked.TreeId.Value, tracked.Left, tracked.Right, tracked.Depth, tracked.Position));
        Assert.Equal(
            0,
            await context
                .Set<BroadTreeIdNode>()
                .AsNoTracking()
                .CountAsync(node => node.Id >= root && node.Id <= root + 1, CancellationToken.None));

        Assert.Equal(
            2,
            await context
                .Set<BroadTreeIdNode>()
                .AsNoTracking()
                .CountAsync(node => node.Id >= root + 10 && node.Id <= root + 11, CancellationToken.None));
    }

    /// <summary>Retains the zero-command rejection for a definite affected provider identity.</summary>
    [Fact]
    public async Task DeleteTreeAsync_DefiniteAffectedIdentity_RejectsWithoutDatabaseCommands()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = $"guard-exact-{root}";
        await SeedTreeAsync(context, treeId, root, 1, cancellationToken: TestContext.Current.CancellationToken);
        var tracked = await ReadNodeAsync(context, root + 1, cancellationToken: TestContext.Current.CancellationToken);
        context.Attach(tracked);
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(treeId), CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(0, probe.Commands);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(tracked)
                .State);
    }

    /// <summary>Transports an unaffected native tracked set beyond the former 64-key boundary in one probe.</summary>
    [Fact]
    public async Task DeleteTreeAsync_UnaffectedTrackedCandidates_UseBoundedNativeBatches()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedTree = $"guard-affected-{root}";
        var otherTree = $"guard-other-{root}";
        await SeedTreeAsync(context, affectedTree, root, 1, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeAsync(
            context,
            otherTree,
            root + 100,
            64,
            cancellationToken: TestContext.Current.CancellationToken);

        var tracked = await context
            .Set<BroadTreeIdNode>()
            .Where(node => node.Id >= root + 100 && node.Id <= root + 164)
            .OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(affectedTree), CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(65, tracked.Length);
        Assert.Equal(1, probe.NativeReads);
        Assert.InRange(probe.MaximumNativeParameters, 1, 2);
        Assert.All(
            tracked,
            node => Assert.Equal(
                EntityState.Unchanged,
                context.Entry(node)
                    .State));
    }

    /// <summary>Protects a persisted affected row even when its tracked TreeId predates an external move.</summary>
    [Fact]
    public async Task DeleteTreeAsync_PersistedMembership_RejectsAnExternallyMovedTrackedNode()
    {
        // Arrange
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var source = $"guard-source-{root}";
        var target = $"guard-target-{root}";
        await SeedTreeAsync(context, source, root, 1, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeAsync(context, target, root + 10, 0, cancellationToken: TestContext.Current.CancellationToken);
        var stale = await ReadNodeAsync(context, root + 1, cancellationToken: TestContext.Current.CancellationToken);
        context.Attach(stale);
        var before = (stale.TreeId.Value, stale.ParentId, stale.Left, stale.Right, stale.Depth, stale.Position);
        await using var writer = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await writer
            .NestedSet<BroadTreeIdNode>()
            .MoveToAsync(root + 1, root + 10, CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(target), CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(stale)
                .State);

        Assert.Equal(
            before,
            (stale.TreeId.Value, stale.ParentId, stale.Left, stale.Right, stale.Depth, stale.Position));

        Assert.Equal(
            target,
            (await ReadNodeAsync(
                context,
                root + 1,
                cancellationToken: TestContext.Current.CancellationToken)).TreeId.Value);

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(target))
                .Nodes
                .CountAsync(CancellationToken.None));
    }
}
