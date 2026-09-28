namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies typed tree-registry lifecycle and ordering on every supported relational engine.</summary>
public abstract class TreeRegistryLockTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates tests backed by fixture-owned provider instances.</summary>
    protected TreeRegistryLockTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>An already active TreeId cannot be reserved as a new tree.</summary>
    [Fact]
    public async Task ActiveTreeIdCannotBeReservedAgain()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                CancellationToken.None);
        }

        await using var context = database.CreateContext();

        // Act
        var exception = await Record.ExceptionAsync(() => TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            NestedSetTreeLockMode.New,
            CancellationToken.None));

        // Assert
        var nestedSet = Assert.IsType<NestedSetException>(exception);
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, nestedSet.Code);
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Locking an existing active tree must not count as a structural change.</summary>
    [Fact]
    public async Task ExistingLockPreservesExistingRevision()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                CancellationToken.None);
        }

        await using var context = database.CreateContext();
        var revisionBefore = await TreeRegistryLockTestSupport.ReadRevisionAsync(context, 7, treeId);

        // Act
        await TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            NestedSetTreeLockMode.Existing,
            CancellationToken.None);

        // Assert
        await using var verification = database.CreateContext();
        Assert.Equal(revisionBefore, await TreeRegistryLockTestSupport.ReadRevisionAsync(verification, 7, treeId));
    }

    /// <summary>Explicitly reserving a new tree creates its registry without a structural revision.</summary>
    [Fact]
    public async Task NewReservationStartsAtRevisionZero()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using var context = database.CreateContext();

        // Act
        await TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            NestedSetTreeLockMode.New,
            CancellationToken.None);

        // Assert
        await using var verification = database.CreateContext();
        Assert.Equal(0, await TreeRegistryLockTestSupport.ReadRevisionAsync(verification, 7, treeId));
    }

    /// <summary>A tombstone rejects operations that require an existing active tree.</summary>
    [Fact]
    public async Task TombstonedTreeIdRejectsExistingLock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                CancellationToken.None);

            await TombstoneAsync(setup, 7, treeId);
        }

        await using var context = database.CreateContext();

        // Act
        var existing = await Record.ExceptionAsync(() => TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            NestedSetTreeLockMode.Existing,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(existing).Code);
    }

    /// <summary>A tombstone rejects attempts to reserve its TreeId as a new tree.</summary>
    [Fact]
    public async Task TombstonedTreeIdRejectsNewReservation()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                CancellationToken.None);

            await TombstoneAsync(setup, 7, treeId);
        }

        await using var context = database.CreateContext();

        // Act
        var created = await Record.ExceptionAsync(() => TreeRegistryLockTestSupport.AcquireAsync(
            context,
            7,
            treeId,
            NestedSetTreeLockMode.New,
            CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(created).Code);
    }

    /// <summary>Fixture seeding rejects a deleted tree identity without persisting precomputed rows.</summary>
    [Fact]
    public async Task FixtureSeedRejectsTombstonedTreeId()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireAsync(
                setup,
                7,
                treeId,
                NestedSetTreeLockMode.New,
                CancellationToken.None);

            await TombstoneAsync(setup, 7, treeId);
        }

        await using var context = database.CreateContext();
        var revisionBefore = await TreeRegistryLockTestSupport.ReadRevisionAsync(context, 7, treeId);

        await context.AddAsync(
            new TreeNode
            {
                NodeId = 1,
                Tree = 7,
                TreeId = treeId,
                Start = 1,
                End = 2,
            },
            CancellationToken.None);

        // Act
        var failure = await Record.ExceptionAsync(() => context.SavePrecomputedHierarchyAsync(CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.TreeIdUnavailable, Assert.IsType<NestedSetException>(failure).Code);
        await using var verification = database.CreateContext();
        Assert.Equal(revisionBefore, await TreeRegistryLockTestSupport.ReadRevisionAsync(verification, 7, treeId));
        Assert.Empty(await verification.Set<TreeNode>().ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Different TreeIds in the same Scope make progress while another tree lock is retained.</summary>
    [EngineFact(
        ExcludedEngines = ["Sqlite"],
        Reason =
            "This staged contender requires independent server row locks; SQLite serializes all database writers.")]
    public async Task DifferentTreesInSameScopeDoNotShareALibraryLock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireManyAsync(
                setup,
                [
                    TreeRegistryLockTestSupport.Request(setup, 7, firstTree, NestedSetTreeLockMode.New),
                    TreeRegistryLockTestSupport.Request(setup, 7, secondTree, NestedSetTreeLockMode.New),
                ],
                CancellationToken.None);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var isolation = NestedSetProviderCapabilities.Resolve(first).RequiredIsolation;
        await using var transaction = await first.Database.BeginTransactionAsync(isolation, CancellationToken.None);
        await NestedSetTreeLocks.AcquireAsync(
            first,
            [TreeRegistryLockTestSupport.Request(first, 7, firstTree, NestedSetTreeLockMode.Existing)],
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Act
        await TreeRegistryLockTestSupport.AcquireAsync(
            second,
            7,
            secondTree,
            NestedSetTreeLockMode.Existing,
            timeout.Token);
        var firstStillOwnsTransaction = ReferenceEquals(transaction, first.Database.CurrentTransaction);
        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        Assert.True(firstStillOwnsTransaction);
        Assert.False(timeout.IsCancellationRequested);
    }

    /// <summary>Opposite request order is canonicalized before either transaction acquires its first row.</summary>
    [Fact]
    public async Task OppositeMultiTreeRequestsCompleteWithoutDeadlock()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var firstTree = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondTree = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await using (var setup = database.CreateContext())
        {
            await TreeRegistryLockTestSupport.AcquireManyAsync(
                setup,
                [
                    TreeRegistryLockTestSupport.Request(setup, 7, firstTree, NestedSetTreeLockMode.New),
                    TreeRegistryLockTestSupport.Request(setup, 7, secondTree, NestedSetTreeLockMode.New),
                ],
                CancellationToken.None);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ascending = TreeRegistryLockTestSupport.LockAfterGateAsync(
            database,
            7,
            [firstTree, secondTree],
            gate.Task,
            timeout.Token);

        var descending = TreeRegistryLockTestSupport.LockAfterGateAsync(
            database,
            7,
            [secondTree, firstTree],
            gate.Task,
            timeout.Token);

        // Act
        gate.SetResult();
        await Task.WhenAll(ascending, descending);

        // Assert
        Assert.False(timeout.IsCancellationRequested);
    }

    /// <summary>A wide scoped forest resolves one global database order with one rowset parameter.</summary>
    [Fact]
    public async Task WideScopedForestResolvesOrderBeyondScalarParameterLimits()
    {
        // Arrange
        // WHY: Each request binds Scope and TreeId; these engine-sized counts cross scalar parameter limits.
        var count = Engine switch
        {
            "Sqlite" => 16_384,
            "PostgreSql" => 32_769,
            "SqlServer" => 2_048,
            _ => 2_048,
        };

        var database = await _fixture.ResetAsync(Engine);
        var probe = new EnterpriseProbe();
        await using var context = database.CreateContext(probe);
        var entityType = context.Model.FindEntityType(typeof(TreeNode))!;
        var requests = new NestedSetTreeLockRequest<Guid, int>[count];
        for (var index = 0; index < count; index++)
        {
            requests[index] = new NestedSetTreeLockRequest<Guid, int>(
                entityType,
                7,
                Guid.NewGuid(),
                NestedSetTreeLockMode.New);
        }

        // Act
        await NestedSetTreeLocks.RequireDistinctAsync(context, requests, CancellationToken.None);

        // Assert
        Assert.Single(probe.Commands);
        Assert.Single(Assert.Single(probe.ParameterNames));
    }

    /// <summary>Typed and JSON rowsets lock string scopes in the same order across their switch threshold.</summary>
    [Fact]
    public async Task RowsetThresholdPreservesStringScopeLockOrder()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var treeId = Guid.NewGuid();
        var comparedScopes = new[] { "a", "B" };
        var scopes = Enumerable
            .Range(0, 254)
            .Select(index => $"m{index:D3}")
            .Concat(comparedScopes)
            .ToArray();

        await using (var setup = database.CreateContext())
        {
            var entityType = setup.Model.FindEntityType(typeof(TextNode))!;
            var reservations = scopes
                .Select(scope => new NestedSetTreeLockRequest<Guid, string>(
                    entityType,
                    scope,
                    treeId,
                    NestedSetTreeLockMode.New))
                .ToArray();

            await TreeRegistryLockTestSupport.AcquireManyAsync(setup, reservations, CancellationToken.None);
        }

        var narrowProbe = new EnterpriseProbe();
        await using var narrow = database.CreateContext(narrowProbe);
        var narrowType = narrow.Model.FindEntityType(typeof(TextNode))!;
        var narrowRequests = comparedScopes
            .Reverse()
            .Select(scope => new NestedSetTreeLockRequest<Guid, string>(
                narrowType,
                scope,
                treeId,
                NestedSetTreeLockMode.Existing))
            .ToArray();

        var wideProbe = new EnterpriseProbe();
        await using var wide = database.CreateContext(wideProbe);
        var wideType = wide.Model.FindEntityType(typeof(TextNode))!;
        var wideRequests = scopes
            .Select(scope => new NestedSetTreeLockRequest<Guid, string>(
                wideType,
                scope,
                treeId,
                NestedSetTreeLockMode.Existing))
            .ToArray();

        // Act
        await TreeRegistryLockTestSupport.AcquireManyAsync(narrow, narrowRequests, CancellationToken.None);
        await TreeRegistryLockTestSupport.AcquireManyAsync(wide, wideRequests, CancellationToken.None);

        // Assert
        var narrowOrder = LockScopes(narrowProbe);
        var wideOrder = LockScopes(wideProbe);

        Assert.Equal(2, narrowOrder.Length);
        Assert.Equal(narrowOrder, wideOrder);
    }

    /// <summary>Extracts only acquired lock identities, excluding rowset ordering-query parameters.</summary>
    private static string[] LockScopes(
        EnterpriseProbe probe
    ) => probe
        .Commands
        .Zip(probe.ParameterValues)
        .Where(command => command.First.Contains(
            NestedSetTreeRegistryMetadata.Lifecycle,
            StringComparison.OrdinalIgnoreCase))
        .SelectMany(command => command.Second)
        .OfType<string>()
        .Where(scope => scope is "a" or "B")
        .ToArray();

    /// <summary>Marks an existing registry identity as deleted through its named shared entity mapping.</summary>
    private static async Task TombstoneAsync(
        TreeContext context,
        int scope,
        Guid treeId
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(TreeNode))!;
        var registry = NestedSetTreeRegistryMapping.For(hierarchy).Registry;

        await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Where(row => EF.Property<int>(row, "Scope") == scope && EF.Property<Guid>(row, "TreeId") == treeId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    row => EF.Property<byte>(row, "Lifecycle"),
                    NestedSetTreeRegistryMetadata.Tombstoned),
                CancellationToken.None);
    }
}
