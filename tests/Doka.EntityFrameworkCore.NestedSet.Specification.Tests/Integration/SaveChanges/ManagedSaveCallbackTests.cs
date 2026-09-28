namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks application callbacks that inspect the tracker after a managed insertion.</summary>
public abstract class ManagedSaveCallbackTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Uses the relational fixture's isolated database for each callback scenario.</summary>
    protected ManagedSaveCallbackTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Post-save event and interceptor callbacks can inspect accepted inserted nodes.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PostSaveTrackerInspectionDoesNotRejectManagedInsert(
        bool bulk,
        bool interceptor
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var callback = new PostSaveInspector();
        await using var context = database.CreateContext(callback);
        var observed = 0;
        callback.OnSaved = current => observed = current
            .ChangeTracker
            .Entries<TreeNode>()
            .Count();

        if (!interceptor)
        {
            callback.OnSaved = null;
            context.SavedChanges += (_, _) => observed = context
                .ChangeTracker
                .Entries<TreeNode>()
                .Count();
        }

        var root = new TreeNode { NodeId = 101 };
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        if (bulk)
        {
            await hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(root)),],
                CancellationToken.None);
        }
        else
        {
            await hierarchy.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
        }

        // Assert
        Assert.Equal(1, observed);
        Assert.Equal(EntityState.Detached, context.Entry(root).State);
        await using var verification = database.CreateContext();
        Assert.Equal(
            1,
            await verification
                .Set<TreeNode>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Every supported provider routes saves through the nested-set persistence boundary.</summary>
    [Fact]
    public async Task PersistenceHookIsInstalledForEveryProvider()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();

        // Act
        var service = context.GetService<IDatabase>();

        // Assert
        Assert.IsType<NestedSetRelationalDatabase>(service);
    }

    /// <summary>A rollback after EF accepted callback writes restores their pending tracker state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RolledBackManagedInsertRestoresAcceptedCallbackWrites(
        bool bulk
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var callback = new SaveBoundaryCallback();
        var failure = new PostSaveFailure();
        await using var context = database.CreateContext(callback, failure);
        var (changed, removed, audit) = await PrepareCallbackWritesAsync(database, context, callback);

        var root = new TreeNode { NodeId = 101 };
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => bulk
            ? hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(root)),],
                CancellationToken.None)
            : hierarchy.InsertRootAsync(root, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Same(failure.Error, error);
        Assert.Equal(EntityState.Detached, context.Entry(root).State);

        var auditEntry = context.Entry(audit);
        Assert.Equal(EntityState.Added, auditEntry.State);
        Assert.True(auditEntry.Property(row => row.Id).IsTemporary);
        Assert.NotEqual(0, auditEntry.Property(row => row.Id).CurrentValue);
        Assert.Equal(0, audit.Id);

        var changedEntry = context.Entry(changed);
        Assert.Equal(EntityState.Modified, changedEntry.State);
        Assert.Equal("stored", changedEntry.Property(row => row.Value).OriginalValue);
        Assert.Equal("changed", changed.Value);
        Assert.Equal(EntityState.Deleted, context.Entry(removed).State);

        await using var verification = database.CreateContext();
        Assert.Empty(
            await verification
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(
            await verification
                .Set<GeneratedAuditRow>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            "stored",
            await verification
                .Set<UnrelatedRow>()
                .Where(row => row.Id == 701)
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
        Assert.True(
            await verification
                .Set<UnrelatedRow>()
                .AnyAsync(row => row.Id == 702, CancellationToken.None));
    }

    /// <summary>Added, modified, and deleted callback rows can be saved again after insertion rolls back.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RolledBackCallbackWritesCanBeSavedAgain(
        bool bulk
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var callback = new SaveBoundaryCallback();
        var failure = new PostSaveFailure();
        await using var context = database.CreateContext(callback, failure);
        var (_, _, audit) = await PrepareCallbackWritesAsync(database, context, callback);
        var root = new TreeNode { NodeId = 101 };
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var error = await Record.ExceptionAsync(() => bulk
            ? hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(root)),],
                CancellationToken.None)
            : hierarchy.InsertRootAsync(root, Guid.Empty, CancellationToken.None));

        // WHY: The restored pending state must be persistable exactly like work from a failed ordinary save.
        callback.OnSaving = null;

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.True(audit.Id > 0);
        await using var retried = database.CreateContext();
        Assert.Equal(
            "inserted",
            Assert.Single(
                    await retried
                        .Set<GeneratedAuditRow>()
                        .ToArrayAsync(CancellationToken.None))
                .Value);
        Assert.Empty(
            await retried
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            "changed",
            await retried
                .Set<UnrelatedRow>()
                .Where(row => row.Id == 701)
                .Select(row => row.Value)
                .SingleAsync(CancellationToken.None));
        Assert.False(
            await retried
                .Set<UnrelatedRow>()
                .AnyAsync(row => row.Id == 702, CancellationToken.None));
    }

    /// <summary>Seeds and tracks callback writes shared by the rollback and retry checks.</summary>
    private static async Task<(UnrelatedRow Changed, UnrelatedRow Removed, GeneratedAuditRow Audit)>
        PrepareCallbackWritesAsync(
            TestDatabase database,
            TreeContext context,
            SaveBoundaryCallback callback
        )
    {
        await using (var setup = database.CreateContext())
        {
            await setup.AddRangeAsync(
                [
                    new UnrelatedRow
                    {
                        Id = 701,
                        Value = "stored",
                    },
                    new UnrelatedRow
                    {
                        Id = 702,
                        Value = "removed",
                    },
                ],
                CancellationToken.None);

            await setup.SaveChangesAsync(CancellationToken.None);
        }

        var changed = await context
            .Set<UnrelatedRow>()
            .SingleAsync(row => row.Id == 701, CancellationToken.None);

        var removed = await context
            .Set<UnrelatedRow>()
            .SingleAsync(row => row.Id == 702, CancellationToken.None);

        var audit = new GeneratedAuditRow { Value = "inserted" };
        callback.OnSaving = current =>
        {
            // WHY: This synchronous SavingChanges callback must finish attaching the audit before it returns.
            // The configured identity generator does not query the database during Add.
            // ReSharper disable once MethodHasAsyncOverload
            current.Add(audit);
            changed.Value = "changed";
            current.Remove(removed);
        };

        return (changed, removed, audit);
    }

    /// <summary>A later bulk-batch failure restores every tree input and generated callback key.</summary>
    /// <param name="independentTrees">Whether both batches span distinct trees instead of one wide subtree.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterBatchFailureRestoresEarlierCallbackWrites(
        bool independentTrees
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        var callback = new SaveBoundaryCallback();
        var failure = new PostSaveFailure { FailOnSave = 2 };
        await using var context = database.CreateContext(callback, failure);
        var audits = new List<GeneratedAuditRow>();
        callback.OnSaving = current =>
        {
            var audit = new GeneratedAuditRow { Value = $"batch-{audits.Count + 1}" };
            audits.Add(audit);
            // WHY: This synchronous SavingChanges callback must finish attaching the audit before it returns.
            // The configured identity generator does not query the database during Add.
            // ReSharper disable once MethodHasAsyncOverload
            current.Add(audit);
        };

        var children = Enumerable
            .Range(2, 64)
            .Select(id => new NestedSetBranch<TreeNode>(new TreeNode { NodeId = id }))
            .ToArray();

        var branch = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);
        var trees = independentTrees
            ? Enumerable
                .Range(1, 65)
                .Select(id => new NestedSetTreeImport<TreeNode, Guid>(
                    Guid.NewGuid(),
                    new NestedSetBranch<TreeNode>(new TreeNode { NodeId = id })))
                .ToArray()
            : [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, branch)];

        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertForestAsync(trees, CancellationToken.None));

        // Assert
        Assert.Same(failure.Error, error);
        Assert.Equal(2, audits.Count);
        Assert.All(
            trees,
            tree =>
            {
                Assert.Equal(Guid.Empty, tree.Root.Entity.TreeId);
                Assert.Equal((0L, 0L), (tree.Root.Entity.Start, tree.Root.Entity.End));
                Assert.Equal(EntityState.Detached, context.Entry(tree.Root.Entity).State);
            });

        Assert.All(
            audits,
            audit =>
            {
                var entry = context.Entry(audit);

                Assert.Equal(0, audit.Id);
                Assert.Equal(EntityState.Added, entry.State);
                Assert.True(entry.Property(row => row.Id).IsTemporary);
                Assert.NotEqual(0, entry.Property(row => row.Id).CurrentValue);
            });

        await using var verification = database.CreateContext();
        Assert.Empty(
            await verification
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(
            await verification
                .Set<GeneratedAuditRow>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A save callback that suppresses persistence cannot report the planned insertion as saved.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuppressedManagedSaveRejectsInsertion(
        bool bulk
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext(new SuppressingSave());
        var root = new TreeNode { NodeId = 101 };
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var error = await Record.ExceptionAsync(() => bulk
            ? hierarchy.InsertForestAsync(
                [new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, new NestedSetBranch<TreeNode>(root)),],
                CancellationToken.None)
            : hierarchy.InsertRootAsync(root, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(EntityState.Detached, context.Entry(root).State);
        await using var verification = database.CreateContext();
        Assert.Empty(
            await verification
                .Set<TreeNode>()
                .ToArrayAsync(CancellationToken.None));
    }

    /// <summary>Runs an application inspection at the asynchronous SavedChanges interceptor boundary.</summary>
    private sealed class PostSaveInspector : SaveChangesInterceptor
    {
        internal Action<DbContext>? OnSaved { get; set; }

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            OnSaved?.Invoke(eventData.Context!);

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Fails once after EF persisted and accepted a save, while the transaction can still roll back.</summary>
    private sealed class PostSaveFailure : SaveChangesInterceptor
    {
        private int _savedCount;

        /// <summary>Gets the one-based save invocation that raises the injected failure.</summary>
        internal int FailOnSave { get; init; } = 1;

        /// <summary>Gets the exact failure raised after the first successful persistence step.</summary>
        internal Exception Error { get; } = new InvalidOperationException("Injected post-save failure.");

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (++_savedCount != FailOnSave)
            {
                return ValueTask.FromResult(result);
            }

            throw Error;
        }
    }

    /// <summary>Replaces the save with a result so EF never reaches its persistence step.</summary>
    private sealed class SuppressingSave : SaveChangesInterceptor
    {
        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }
}
