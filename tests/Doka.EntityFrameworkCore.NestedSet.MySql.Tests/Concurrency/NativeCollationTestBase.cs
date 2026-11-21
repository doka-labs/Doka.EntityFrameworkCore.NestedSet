namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Exercises the Doka family's native Scope collation and padding semantics.</summary>
public abstract partial class NativeCollationTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares the provider collection's isolated compatibility database.</summary>
    /// <param name="fixture">The engine-bound compatibility fixture.</param>
    protected NativeCollationTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Configured binary table collations distinguish Scope aliases on both key transport paths.</summary>
    /// <param name="convertedKeys">Whether NodeKey transport uses the bounded converted-key fallback.</param>
    /// <returns>A task completing after native comparison and exact tracked-state checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_InheritedBinaryScopeCollation_KeepsUnaffectedDuplicateKey(
        bool convertedKeys
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateInheritedScopeContextAsync(
            Engine,
            convertedKeys,
            false,
            probe,
            cancellationToken: TestContext.Current.CancellationToken);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var trackedScope = new BroadScope($"GUARD-INHERITED-{root}");
        var requestedScope = new BroadScope(trackedScope.Value.ToLowerInvariant());
        var trackedTree = Guid.NewGuid();
        var requestedTree = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            trackedScope,
            trackedTree,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            requestedScope,
            requestedTree,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        var tracked = await context
            .Set<BroadScopeNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Scope == trackedScope && node.Id == root + 1, CancellationToken.None);

        context.Attach(tracked);
        var before = (tracked.Scope.Value, tracked.Id, tracked.TreeId, tracked.ParentId,
            tracked.Left, tracked.Right, tracked.Depth, tracked.Position, tracked.Name);

        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            requestedScope,
            requestedTree,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var operationCalls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                operationCalls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Null(failure);
        AssertInheritedTableCollation(context, entityType, "utf8mb4_bin");
        Assert.True(trackedScope.Equals(requestedScope));
        Assert.Equal(1, probe.NativeReads);
        Assert.Equal(1, probe.NativeScopeComparisons);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(1, operationCalls);
        Assert.Equal(
            before,
            (tracked.Scope.Value, tracked.Id, tracked.TreeId, tracked.ParentId,
                tracked.Left, tracked.Right, tracked.Depth, tracked.Position, tracked.Name));

        Assert.Single(context.ChangeTracker.Entries<BroadScopeNode>());
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(tracked)
                .State);

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(trackedScope)
                .InTree(trackedTree)
                .Nodes
                .CountAsync(CancellationToken.None));

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(requestedScope)
                .InTree(requestedTree)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A PAD SPACE Scope alias still identifies its persisted affected row.</summary>
    [Fact]
    public async Task ExecuteAsync_NativePadSpaceScopeAlias_RejectsAffectedStaleMembership()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await _fixture.CreateContextAsync<ScopedScopeAliasContext>(
            Engine,
            static options => new ScopedScopeAliasContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var scope = $"guard-pad-space-{root}";
        var treeId = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedScopedNativeAsync(
            context,
            scope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        var persisted = await context
            .Set<ScopeAliasNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Scope == scope && node.Id == root + 1, CancellationToken.None);

        var alias = persisted.WithScope(scope + " ");
        alias.TreeId = Guid.NewGuid();
        context.Attach(alias);
        var before = (alias.Scope, alias.Id, alias.TreeId, alias.ParentId,
            alias.Left, alias.Right, alias.Depth, alias.Position, alias.Name);

        var entityType = context.Model.FindEntityType(typeof(ScopeAliasNode))!;
        var request = new NestedSetTreeLockRequest<Guid, string>(
            entityType,
            scope,
            treeId,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<ScopeAliasNode, int, Guid, string>(context, entityType);
        var operationCalls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                operationCalls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(1, probe.NativeReads);
        Assert.Equal(1, probe.NativeScopeComparisons);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(0, operationCalls);
        Assert.Equal(
            before,
            (alias.Scope, alias.Id, alias.TreeId, alias.ParentId,
                alias.Left, alias.Right, alias.Depth, alias.Position, alias.Name));

        Assert.Single(context.ChangeTracker.Entries<ScopeAliasNode>());
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(alias)
                .State);

        Assert.Equal(
            2,
            await context
                .NestedSet<ScopeAliasNode>()
                .ForScope(scope)
                .InTree(treeId)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>An unknown database-default case alias still identifies an affected stale tracked row.</summary>
    /// <param name="convertedKeys">Whether NodeKey transport uses the bounded converted-key fallback.</param>
    /// <returns>A task that completes after proving native rejection and exact state preservation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_UnknownDatabaseScopeAlias_RejectsAffectedStaleMembership(
        bool convertedKeys
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();

        // WHY: This scenario tests the database default itself. Other compatibility models legitimately change
        // their database's default collation, so sharing only distinct tables would make this premise order-dependent.
        // The isolated schema still reuses the assembly-owned server container.
        await using var database = await TestDatabase.CreateAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken);

        await using BroadScopeContext context = convertedKeys
            ? await CreateUnknownDatabaseContextAsync<ConvertedUnknownDatabaseScopeContext>(
                database,
                static options => new ConvertedUnknownDatabaseScopeContext(options),
                probe,
                cancellationToken: TestContext.Current.CancellationToken)
            : await CreateUnknownDatabaseContextAsync<UnknownDatabaseScopeContext>(
                database,
                static options => new UnknownDatabaseScopeContext(options),
                probe,
                cancellationToken: TestContext.Current.CancellationToken);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var persistedScope = new BroadScope($"GUARD-UNKNOWN-DEFAULT-{root}");
        var aliasScope = new BroadScope(persistedScope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        await NativeTrackedIdentityGuardTestSupport.SeedBroadScopeAsync(
            context,
            persistedScope,
            treeId,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        // WHY: One ordinary Scope predicate proves the real database default accepts this case alias. No model
        // collation or CLR equality is used to assume that premise for arbitrary server defaults.
        var alias = await context
            .Set<BroadScopeNode>()
            .AsNoTracking()
            .SingleAsync(node => node.Scope == aliasScope && node.Id == root + 1, CancellationToken.None);

        var storedScope = alias.Scope.Value;
        alias.Scope = aliasScope;
        alias.TreeId = Guid.NewGuid();
        context.Attach(alias);
        var before = ScopeSnapshot(alias);
        var rowsBefore = await BroadScopeRowsAsync(
            context,
            root,
            cancellationToken: TestContext.Current.CancellationToken);

        var registryBefore = await BroadScopeRegistriesAsync(
            context,
            treeId,
            cancellationToken: TestContext.Current.CancellationToken);

        var entityType = context.Model.FindEntityType(typeof(BroadScopeNode))!;
        var request = new NestedSetTreeLockRequest<Guid, BroadScope>(
            entityType,
            persistedScope,
            treeId,
            NestedSetTreeLockMode.Existing);

        var executor = new NestedSetMutationExecutor<BroadScopeNode, int, Guid, BroadScope>(context, entityType);
        var operationCalls = 0;
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            _ =>
            {
                operationCalls++;

                return Task.CompletedTask;
            },
            [request],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        var designModel = context.GetService<IDesignTimeModel>()
            .Model;

        var designEntity = designModel.FindEntityType(entityType.Name)!;
        Assert.Null(designModel.GetCollation());
        Assert.Null(designEntity.FindProperty(nameof(BroadScopeNode.Scope))!.GetCollation());
        Assert.Null(
            designEntity.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);

        Assert.Null(
            NestedSetCollations.Resolve(
                context,
                entityType.FindProperty(nameof(BroadScopeNode.Scope))!,
                entityType));

        Assert.Equal(persistedScope.Value, storedScope);
        Assert.NotEqual(storedScope, aliasScope.Value);
        Assert.Equal(1, probe.NativeReads);
        Assert.Equal(1, probe.NativeScopeComparisons);
        Assert.InRange(probe.MaximumNativeParameters, 1, 4);
        Assert.Equal(0, operationCalls);
        Assert.Equal(before, ScopeSnapshot(alias));
        Assert.Single(context.ChangeTracker.Entries<BroadScopeNode>());
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(alias)
                .State);

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(
            rowsBefore,
            await BroadScopeRowsAsync(
                context,
                root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            registryBefore,
            await BroadScopeRegistriesAsync(
                context,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Verifies table inheritance is known despite absent property and model collation settings.</summary>
    /// <param name="context">The finalized model used by the native guard.</param>
    /// <param name="entityType">The selected concrete hierarchy mapping.</param>
    /// <param name="expected">The collation inherited from its physical table.</param>
    private protected static void AssertInheritedTableCollation(
        DbContext context,
        IEntityType entityType,
        string expected
    )
    {
        var designModel = context.GetService<IDesignTimeModel>()
            .Model;

        var designEntity = designModel.FindEntityType(entityType.Name)!;
        Assert.Null(designModel.GetCollation());
        Assert.Null(designEntity.FindProperty(nameof(BroadScopeNode.Scope))!.GetCollation());
        Assert.Equal(
            expected,
            designEntity.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);

        Assert.Equal(
            expected,
            NestedSetCollations.Resolve(
                context,
                entityType.FindProperty(nameof(BroadScopeNode.Scope))!,
                entityType));
    }
}
