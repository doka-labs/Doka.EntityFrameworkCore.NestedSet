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
    public async Task NativeScopeAliasCannotLeaveAffectedTrackedStructureStale(
        bool scopedKey
    )
    {
        // Arrange
        await using DbContext context = scopedKey
            ? await _fixture.CreateContextAsync<ScopedScopeAliasContext>(
                Engine,
                static options => new ScopedScopeAliasContext(options))
            : await _fixture.CreateContextAsync<ScalarScopeAliasContext>(
                Engine,
                static options => new ScalarScopeAliasContext(options));

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
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(alias).State);
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
    public async Task NativeTreeAliasCannotLeaveAffectedTrackedStructureStale()
    {
        // Arrange
        await using var context = await CreateAliasContextAsync(Engine);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = $"guard-tree-{root}";
        await SeedTreeAsync(context, treeId, root, 1);
        var alias = await ReadNodeAsync(context, root + 1);
        alias.TreeId = new BroadTreeId(treeId.ToUpperInvariant());
        context.Attach(alias);
        var before = (alias.Left, alias.Right, alias.Depth, alias.Position, alias.ParentId);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(treeId), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(alias).State);
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
    public async Task BroadModelComparerAllowsAnUnaffectedProviderDistinctTree()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<BroadGuardTreeContext>(
            Engine,
            static options => new BroadGuardTreeContext(options));

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var first = $"GUARD-{root}";
        var second = first.ToLowerInvariant();
        await SeedTreeAsync(context, first, root, 1);
        await SeedTreeAsync(context, second, root + 10, 1);
        var tracked = await ReadNodeAsync(context, root + 11);
        context.Attach(tracked);
        var before = (tracked.TreeId.Value, tracked.Left, tracked.Right, tracked.Depth, tracked.Position);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(first), CancellationToken.None));

        // Assert
        Assert.Null(failure);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
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
    public async Task DefiniteAffectedIdentityRejectsWithoutDatabaseCommands()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = $"guard-exact-{root}";
        await SeedTreeAsync(context, treeId, root, 1);
        var tracked = await ReadNodeAsync(context, root + 1);
        context.Attach(tracked);
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(treeId), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, probe.Commands);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
    }

    /// <summary>Transports an unaffected native tracked set beyond the former 64-key boundary in one probe.</summary>
    [Fact]
    public async Task UnaffectedTrackedCandidatesUseBoundedNativeBatches()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedTree = $"guard-affected-{root}";
        var otherTree = $"guard-other-{root}";
        await SeedTreeAsync(context, affectedTree, root, 1);
        await SeedTreeAsync(context, otherTree, root + 100, 64);
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
            node => Assert.Equal(EntityState.Unchanged, context.Entry(node).State));
    }

    /// <summary>Rechecks changes or new tracked aliases introduced while a native guard read is awaiting.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeProbeCannotIntroduceUnplannedTrackerChanges(
        bool attachAffectedAlias
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedTree = $"guard-callback-{root}";
        var otherTree = $"guard-unaffected-{root}";
        await SeedTreeAsync(context, affectedTree, root, 1);
        await SeedTreeAsync(context, otherTree, root + 10, 1);
        var affected = await ReadNodeAsync(context, root + 1);
        affected.TreeId = new BroadTreeId(affectedTree.ToUpperInvariant());
        var other = await ReadNodeAsync(context, root + 11);
        context.Attach(other);
        probe.BeforeNativeRead = () =>
        {
            if (attachAffectedAlias)
            {
                context.Attach(affected);
            }
            else
            {
                other.Name = "Callback change";
            }
        };

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(affectedTree), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, probe.NativeReads);
        Assert.Equal(
            2,
            await context
                .Set<BroadTreeIdNode>()
                .AsNoTracking()
                .CountAsync(node => node.Id >= root && node.Id <= root + 1, CancellationToken.None));
        Assert.Equal("Child-000", (await ReadNodeAsync(context, root + 11)).Name);
        Assert.Equal(attachAffectedAlias ? EntityState.Unchanged : EntityState.Modified, context.Entry(other).State);
    }

    /// <summary>Rejects hierarchy attachments during registry locking when no hierarchy entries were tracked.</summary>
    [Fact]
    public async Task RegistryLockCannotAttachAnUnplannedAffectedHierarchyEntry()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = $"guard-lock-{root}";
        await SeedTreeAsync(context, treeId, root, 1);
        var alias = await ReadNodeAsync(context, root + 1);
        probe.RegistryTable = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(BroadTreeIdNode))!).Store.Name;
        probe.AfterRegistryRead = () => context.Attach(alias);
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(treeId), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, probe.RegistryCallbacks);
        Assert.Equal(0, probe.NativeReads);
        Assert.Equal(EntityState.Unchanged, context.Entry(alias).State);
        Assert.Equal(
            2,
            await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(treeId))
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Protects a persisted affected row even when its tracked TreeId predates an external move.</summary>
    [Fact]
    public async Task PersistedMembershipRejectsAnExternallyMovedTrackedNode()
    {
        // Arrange
        await using var context = await CreateAliasContextAsync(Engine);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var source = $"guard-source-{root}";
        var target = $"guard-target-{root}";
        await SeedTreeAsync(context, source, root, 1);
        await SeedTreeAsync(context, target, root + 10, 0);
        var stale = await ReadNodeAsync(context, root + 1);
        context.Attach(stale);
        var before = (stale.TreeId.Value, stale.ParentId, stale.Left, stale.Right, stale.Depth, stale.Position);
        await using var writer = await CreateAliasContextAsync(Engine);
        await writer
            .NestedSet<BroadTreeIdNode>()
            .MoveToAsync(root + 1, root + 10, CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(target), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(EntityState.Unchanged, context.Entry(stale).State);
        Assert.Equal(
            before,
            (stale.TreeId.Value, stale.ParentId, stale.Left, stale.Right, stale.Depth, stale.Position));
        Assert.Equal(target, (await ReadNodeAsync(context, root + 1)).TreeId.Value);
        Assert.Equal(
            2,
            await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(target))
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Validates all exact generic role types before a constructor can execute any database command.</summary>
    [Theory]
    [InlineData("key")]
    [InlineData("scope")]
    [InlineData("tree")]
    public async Task ExecutorRejectsIncorrectGenericIdentityRoles(
        string role
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var entityType = context.Model.FindEntityType(typeof(BroadTreeIdNode))!;
        probe.Armed = true;

        // Act
        var failure = Record.Exception(() =>
        {
            switch (role)
            {
                case "key":
                    _ = new NestedSetMutationExecutor<BroadTreeIdNode, long, BroadTreeId, NestedSetNoScope>(
                        context,
                        entityType);
                    break;
                case "scope":
                    _ = new NestedSetMutationExecutor<BroadTreeIdNode, int, BroadTreeId, string>(context, entityType);
                    break;
                case "tree":
                    _ = new NestedSetMutationExecutor<BroadTreeIdNode, int, string, NestedSetNoScope>(
                        context,
                        entityType);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        });

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, probe.Commands);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Rejects foreign model lock requests on both consistent reads and explicit writes before SQL.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutorRejectsForeignHierarchyRequests(
        bool read
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        await using var foreign = await _fixture.CreateContextAsync<BroadGuardTreeContext>(
            Engine,
            static options => new BroadGuardTreeContext(options));

        var executor = new NestedSetMutationExecutor<BroadTreeIdNode, int, BroadTreeId, NestedSetNoScope>(
            context,
            context.Model.FindEntityType(typeof(BroadTreeIdNode))!);

        var requests = new[]
        {
            new NestedSetTreeLockRequest<BroadTreeId, NestedSetNoScope>(
                foreign.Model.FindEntityType(typeof(BroadTreeIdNode))!,
                default,
                new BroadTreeId("foreign-request"),
                NestedSetTreeLockMode.Existing),
        };

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => read
            ? executor.ExecuteReadAsync(_ => Task.CompletedTask, requests, CancellationToken.None)
            : executor.ExecuteAsync(_ => Task.CompletedTask, requests, CancellationToken.None));

        // Assert
        Assert.Equal("requests", Assert.IsType<ArgumentException>(failure).ParamName);
        Assert.Equal(0, probe.Commands);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Creates the case-insensitive TreeId model with observers local to this operation.</summary>
    private Task<AliasGuardTreeContext> CreateAliasContextAsync(
        string engine,
        params IInterceptor[] interceptors
    ) => _fixture.CreateContextAsync<AliasGuardTreeContext>(
        engine,
        static options => new AliasGuardTreeContext(options),
        interceptors);

    /// <summary>Imports one root with independent keys and clears only arrangement-owned tracking.</summary>
    private static async Task SeedTreeAsync(
        DbContext context,
        string treeId,
        int root,
        int children
    )
    {
        var branches = Enumerable
            .Range(0, children)
            .Select(index => new NestedSetBranch<BroadTreeIdNode>(
                new BroadTreeIdNode
                {
                    Id = root + index + 1,
                    Name = $"Child-{index:D3}",
                }))
            .ToArray();

        var branch = new NestedSetBranch<BroadTreeIdNode>(
            new BroadTreeIdNode
            {
                Id = root,
                Name = "Root",
            },
            branches);

        await context
            .NestedSet<BroadTreeIdNode>()
            .InsertForestAsync(
                [new NestedSetTreeImport<BroadTreeIdNode, BroadTreeId>(new BroadTreeId(treeId), branch)],
                CancellationToken.None);

        context.ChangeTracker.Clear();
    }

    /// <summary>Reads an independent entity snapshot without triggering identity-map alias handling.</summary>
    private static Task<BroadTreeIdNode> ReadNodeAsync(
        DbContext context,
        int id
    ) => context
        .Set<BroadTreeIdNode>()
        .AsNoTracking()
        .SingleAsync(node => node.Id == id, CancellationToken.None);

    /// <summary>Maps native identity equality independently from the model's value comparer.</summary>
    private abstract class GuardTreeContext(DbContextOptions options) : DbContext(options)
    {
        /// <summary>Chooses the store's native case comparison for this concrete test model.</summary>
        protected abstract bool CaseInsensitive { get; }

        /// <summary>Chooses the intentionally broader model comparer for the positive control.</summary>
        protected abstract bool BroadComparer { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: Each concrete model creates its own tables in the shared fixture database. Distinct
            // collation names preserve identical CI semantics without duplicate CREATE COLLATION statements.
            var postgresCollation = GetType()
                    .Name
                    .ToLowerInvariant()
                + "_tree_ci";

            if (CaseInsensitive && Database.IsNpgsql())
            {
                modelBuilder.HasCollation(
                    postgresCollation,
                    locale: "und-u-ks-level2",
                    provider: "icu",
                    deterministic: false);
            }

            var collation = CaseInsensitive
                ? Database.IsSqlite()
                    ? "NOCASE"
                    : Database.IsNpgsql()
                        ? postgresCollation
                        : Database.IsSqlServer()
                            ? "Latin1_General_100_CI_AS"
                            : "utf8mb4_general_ci"
                : Database.IsSqlite()
                    ? "BINARY"
                    : Database.IsSqlServer()
                        ? "Latin1_General_100_BIN2"
                        : Database.IsNpgsql()
                            ? null
                            : "utf8mb4_bin";

            var node = modelBuilder.Entity<BroadTreeIdNode>();
            node.ToTable(
                GetType()
                    .Name
                + "Nodes");
            node.HasKey(value => value.Id);
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.Name)
                .HasMaxLength(100);
            var tree = node
                .Property(value => value.TreeId)
                .HasConversion(value => value.Value, value => new BroadTreeId(value))
                .HasMaxLength(40);

            if (collation is not null)
            {
                tree.UseCollation(collation);
            }

            // WHY: The negative control needs narrow CLR equality over a native CI column; the positive control
            // intentionally reverses that relationship. Neither comparer can stand in for database identity.
            tree.Metadata.SetValueComparer(
                BroadComparer
                    ? new ValueComparer<BroadTreeId>(
                        (
                            first,
                            second
                        ) => StringComparer.OrdinalIgnoreCase.Equals(first!.Value, second!.Value),
                        value => StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value),
                        value => new BroadTreeId(value.Value))
                    : new ValueComparer<BroadTreeId>(
                        (
                            first,
                            second
                        ) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
                        value => StringComparer.Ordinal.GetHashCode(value.Value),
                        value => new BroadTreeId(value.Value)));

            node
                .HasOne<BroadTreeIdNode>()
                .WithMany()
                .HasForeignKey(value => value.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            node.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(value => value.Id)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .HasBounds(value => value.Left, value => value.Right)
                .HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));
        }
    }

    /// <summary>Stores case-insensitive TreeIds while retaining exact CLR value snapshots.</summary>
    private sealed class AliasGuardTreeContext(DbContextOptions<AliasGuardTreeContext> options)
        : GuardTreeContext(options)
    {
        /// <inheritdoc />
        protected override bool CaseInsensitive => true;

        /// <inheritdoc />
        protected override bool BroadComparer => false;
    }

    /// <summary>Stores case-sensitive TreeIds despite a broad domain and metadata value comparer.</summary>
    private sealed class BroadGuardTreeContext(DbContextOptions<BroadGuardTreeContext> options)
        : GuardTreeContext(options)
    {
        /// <inheritdoc />
        protected override bool CaseInsensitive => false;

        /// <inheritdoc />
        protected override bool BroadComparer => true;
    }
}
