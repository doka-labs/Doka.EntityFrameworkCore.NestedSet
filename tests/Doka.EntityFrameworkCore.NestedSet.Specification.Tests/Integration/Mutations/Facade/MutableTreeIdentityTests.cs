namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies binary tree isolation and asynchronous caller-mutation safety on every provider.</summary>
public abstract class MutableTreeIdentityTests : ProviderTest
{
    private readonly BinaryIdentityFixture _fixture;

    /// <summary>Creates tests backed by isolated databases on the assembly-owned provider servers.</summary>
    protected MutableTreeIdentityTests(
        IProviderFixture<BinaryIdentityFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A caller cannot redirect detachment by mutating its TreeId while source resolution awaits.</summary>
    [Fact]
    public async Task DetachSnapshotsTargetBeforeAwaitingSourceResolution()
    {
        // Arrange
        var barrier = new SourceResolutionBarrier();
        await using var context = await _fixture.CreateContextAsync(Engine, barrier);
        var hierarchy = context
            .NestedSet<BinaryTreeNode>()
            .ForScope(7);

        byte[] sourceTreeId = [1, 2, 3, 4];
        byte[] targetTreeId = [5, 6, 7, 8];
        var expectedTarget = targetTreeId.ToArray();
        await hierarchy.InsertRootAsync(new BinaryTreeNode { Id = 1 }, sourceTreeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 3 }, 2, CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        barrier.Arm();

        // Act
        var detachment = hierarchy.DetachAsTreeAsync(2, targetTreeId, timeout.Token);
        await barrier.Attempted.Task.WaitAsync(timeout.Token);

        try
        {
            targetTreeId[0] = 9;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await detachment;
        var detached = await hierarchy
            .InTree(expectedTarget)
            .Nodes
            .ToArrayAsync(timeout.Token);

        var redirectedRows = await hierarchy
            .InTree(targetTreeId)
            .Nodes
            .AnyAsync(timeout.Token);

        var registry = context
            .Model
            .GetEntityTypes()
            .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

        var registryIds = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .Select(row => EF.Property<byte[]>(row, NestedSetTreeRegistryMetadata.TreeId))
            .ToArrayAsync(timeout.Token);

        // Assert
        Assert.Collection(
            detached,
            root => Assert.Equal((2, null, 1L, 4L, 0), (root.Id, root.ParentId, root.Left, root.Right, root.Depth)),
            child => Assert.Equal(
                (3, (int?)2, 2L, 3L, 1),
                (child.Id, child.ParentId, child.Left, child.Right, child.Depth)));
        Assert.All(detached, node => Assert.Equal(expectedTarget, node.TreeId));
        Assert.False(redirectedRows);
        Assert.Equal(2, registryIds.Length);
        Assert.Contains(registryIds, identity => identity.SequenceEqual(expectedTarget));
        Assert.DoesNotContain(registryIds, identity => identity.SequenceEqual(targetTreeId));
    }

    /// <summary>Binary predicates isolate mutations and filtered anchored queries across overlapping trees.</summary>
    [Fact]
    public async Task BinaryStoresIsolateOverlappingTreeBounds()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine);
        var hierarchy = context
            .NestedSet<BinaryTreeNode>()
            .ForScope(7);

        byte[] firstTree = [1, 2, 3, 4];
        byte[] secondTree = [5, 6, 7, 8];
        await hierarchy.InsertRootAsync(new BinaryTreeNode { Id = 1 }, firstTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertRootAsync(new BinaryTreeNode { Id = 10 }, secondTree, CancellationToken.None);
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 20 }, 10, CancellationToken.None);
        await context
            .NestedSet<BinaryTreeNode>()
            .ForScope(9)
            .InsertRootAsync(new BinaryTreeNode { Id = 100 }, firstTree, CancellationToken.None);

        var metadata = context.Model.FindEntityType(typeof(BinaryTreeNode))!;
        var store = new NestedSetStore<BinaryTreeNode, int, byte[], int>(context, metadata, 7, firstTree);

        // Act
        await hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 3 }, 2, CancellationToken.None);
        var selected = await store
            .Nodes
            .OrderBy(node => node.Left)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var filtered = await hierarchy
            .TreeContaining(3)
            .Where(node => node.Depth > 0)
            .Select(node => node.Id)
            .ToArrayAsync(CancellationToken.None);

        var otherTree = await hierarchy
            .InTree(secondTree)
            .Nodes
            .ToArrayAsync(CancellationToken.None);

        var otherScope = await context
            .NestedSet<BinaryTreeNode>()
            .ForScope(9)
            .InTree(firstTree)
            .Nodes
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2, 3], selected);
        Assert.Equal([2, 3], filtered);
        Assert.Collection(
            otherTree,
            root => Assert.Equal((10, 1L, 4L, 0), (root.Id, root.Left, root.Right, root.Depth)),
            child => Assert.Equal((20, 2L, 3L, 1), (child.Id, child.Left, child.Right, child.Depth)));
        Assert.Equal((100, 1L, 2L, 0), (otherScope.Id, otherScope.Left, otherScope.Right, otherScope.Depth));
    }

    /// <summary>The post-lookup guard detects CLR edits introduced while the identity query awaits.</summary>
    [Fact]
    public async Task ChangesDuringIdentityResolutionAreRejectedBeforeLocksAndWrites()
    {
        // Arrange
        var barrier = new SourceResolutionBarrier();
        var commands = new CommandProbe();
        await using var context = await _fixture.CreateContextAsync(Engine, barrier, commands);
        var hierarchy = context
            .NestedSet<BinaryTreeNode>()
            .ForScope(7);

        byte[] treeId = [1, 2, 3, 4];
        await hierarchy.InsertRootAsync(new BinaryTreeNode { Id = 1 }, treeId, CancellationToken.None);
        await context
            .NestedSet<BinaryTreeNode>()
            .ForScope(9)
            .InsertRootAsync(new BinaryTreeNode { Id = 100 }, treeId, CancellationToken.None);

        var tracked = await context
            .Set<BinaryTreeNode>()
            .SingleAsync(node => node.Id == 100, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        commands.Reset();
        barrier.Arm();

        // Act
        var insertion = hierarchy.InsertChildAsync(new BinaryTreeNode { Id = 2 }, 1, timeout.Token);
        await barrier.Attempted.Task.WaitAsync(timeout.Token);

        try
        {
            // WHY: No explicit state assignment or DetectChanges occurs here. The late mutation guard must
            // discover this ordinary CLR edit after the asynchronous lookup and before acquiring any locks.
            tracked.Payload = "changed during lookup";
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        var failure = await Record.ExceptionAsync(() => insertion);
        var commandCount = commands.CommandCount;
        var updates = commands.UpdateCount;
        var rows = await context
            .Set<BinaryTreeNode>()
            .AsNoTracking()
            .OrderBy(node => node.Id)
            .ToArrayAsync(timeout.Token);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(1, commandCount);
        Assert.Equal(0, updates);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
        Assert.Equal("changed during lookup", tracked.Payload);
        Assert.Collection(
            rows,
            source => Assert.Equal((1, 1L, 2L, "original"), (source.Id, source.Left, source.Right, source.Payload)),
            unrelated => Assert.Equal(
                (100, 1L, 2L, "original"),
                (unrelated.Id, unrelated.Left, unrelated.Right, unrelated.Payload)));
    }

    /// <summary>Changing source and destination arrays cannot redirect a move within the discovered tree.</summary>
    [Fact]
    public async Task MoveSnapshotsBinarySourceAndDestinationKeys()
    {
        // Arrange
        var barrier = new BinaryKeyResolutionBarrier();
        await using var context = await _fixture.CreateKeyContextAsync(Engine, barrier);
        var hierarchy = context
            .NestedSet<BinaryNode>()
            .ForScope(7);

        await SeedBinarySiblingsAsync(hierarchy);
        byte[] source = [2, 2];
        byte[] destination = [4, 4];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        barrier.Arm();

        // Act
        var move = hierarchy.MoveToAsync(source, destination, timeout.Token);
        await barrier.WaitForResolutionAsync(move, timeout.Token);

        try
        {
            // WHY: Every alias stays in the same tree, so checking TreeId membership alone cannot detect
            // caller retargeting. The requested node and anchor must retain their original key bytes.
            source[0] = source[1] = 3;
            destination[0] = destination[1] = 1;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await move;
        var nodes = await hierarchy
            .InTree(Guid.Empty)
            .Nodes
            .ToArrayAsync(timeout.Token);

        // Assert
        var intended = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([2, 2]));
        var alias = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([3, 3]));
        var intendedParent = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([4, 4]));
        Assert.Equal<byte>([4, 4], intended.ParentId!);
        Assert.Equal(2, intended.Depth);
        Assert.Equal<byte>([1, 1], alias.ParentId!);
        Assert.Equal(1, alias.Depth);
        Assert.Equal<byte>([1, 1], intendedParent.ParentId!);
        Assert.Equal(1, intendedParent.Depth);
    }

    /// <summary>Changing a binary parent argument cannot attach the new child to another same-tree sibling.</summary>
    [Fact]
    public async Task InsertSnapshotsBinaryParentKey()
    {
        // Arrange
        var barrier = new BinaryKeyResolutionBarrier();
        await using var context = await _fixture.CreateKeyContextAsync(Engine, barrier);
        var hierarchy = context
            .NestedSet<BinaryNode>()
            .ForScope(7);

        await SeedBinarySiblingsAsync(hierarchy);
        byte[] parent = [2, 2];
        var inserted = new BinaryNode { Id = [5, 5] };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        barrier.Arm();

        // Act
        var insertion = hierarchy.InsertChildAsync(inserted, parent, timeout.Token);
        await barrier.WaitForResolutionAsync(insertion, timeout.Token);

        try
        {
            parent[0] = parent[1] = 3;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await insertion;
        var nodes = await hierarchy
            .InTree(Guid.Empty)
            .Nodes
            .ToArrayAsync(timeout.Token);

        // Assert
        var persisted = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([5, 5]));
        var alias = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([3, 3]));
        Assert.Equal<byte>([2, 2], inserted.ParentId!);
        Assert.Equal<byte>([2, 2], persisted.ParentId!);
        Assert.Equal(2, persisted.Depth);
        Assert.Equal(alias.Left + 1, alias.Right);
    }

    /// <summary>Changing a binary source argument cannot delete a different node with the same TreeId.</summary>
    [Fact]
    public async Task DeleteSnapshotsBinarySourceKey()
    {
        // Arrange
        var barrier = new BinaryKeyResolutionBarrier();
        await using var context = await _fixture.CreateKeyContextAsync(Engine, barrier);
        var hierarchy = context
            .NestedSet<BinaryNode>()
            .ForScope(7);

        await SeedBinarySiblingsAsync(hierarchy);
        byte[] source = [2, 2];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        barrier.Arm();

        // Act
        var deletion = hierarchy.DeleteSubtreeAsync(source, timeout.Token);
        await barrier.WaitForResolutionAsync(deletion, timeout.Token);

        try
        {
            source[0] = source[1] = 3;
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await deletion;
        var nodes = await hierarchy
            .InTree(Guid.Empty)
            .Nodes
            .ToArrayAsync(timeout.Token);

        // Assert
        Assert.Equal(3, nodes.Length);
        Assert.DoesNotContain(nodes, node => node.Id.SequenceEqual<byte>([2, 2]));
        var alias = Assert.Single(nodes, node => node.Id.SequenceEqual<byte>([3, 3]));
        Assert.Equal<byte>([1, 1], alias.ParentId!);
        Assert.Equal(1, alias.Depth);
    }

    /// <summary>Creates three independently addressable siblings beneath one binary-key root.</summary>
    private static async Task SeedBinarySiblingsAsync(
        ScopedNestedSet<BinaryNode, int> hierarchy
    )
    {
        await hierarchy.InsertRootAsync(new BinaryNode { Id = [1, 1] }, Guid.Empty, CancellationToken.None);
        await hierarchy.InsertChildAsync<byte[]>(new BinaryNode { Id = [2, 2] }, [1, 1], CancellationToken.None);
        await hierarchy.InsertChildAsync<byte[]>(new BinaryNode { Id = [3, 3] }, [1, 1], CancellationToken.None);
        await hierarchy.InsertChildAsync<byte[]>(new BinaryNode { Id = [4, 4] }, [1, 1], CancellationToken.None);
    }

    /// <summary>Owns additional binary-identity tables in independently allocated test databases.</summary>
    public sealed class BinaryIdentityFixture : IAsyncLifetime
    {
        private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DbContextOptions> _options = new(StringComparer.Ordinal);
        private readonly RelationalFixture _keyFixture = new();

        /// <inheritdoc />
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        /// <summary>Uses the existing binary-node model in a separate fixture-owned database.</summary>
        internal async Task<TreeContext> CreateKeyContextAsync(
            string engine,
            params IInterceptor[] interceptors
        )
        {
            var database = await _keyFixture.ResetAsync(engine);

            return database.CreateContext(interceptors);
        }

        /// <summary>Returns a reset binary model using the existing provider configuration and lifecycle.</summary>
        internal async Task<DbContext> CreateContextAsync(
            string engine,
            params IInterceptor[] interceptors
        )
        {
            if (!_databases.TryGetValue(engine, out var database))
            {
                // WHY: Reuse the existing provider and resource owner with this test's model initializer.
                // Only our tables are created; the assembly-owned container and other classes stay untouched.
                database = await TestDatabase.CreateAsync(
                    engine,
                    async source =>
                    {
                        var extensions = source
                            .GetService<IDbContextOptions>()
                            .Extensions
                            .ToDictionary(extension => extension.GetType());

                        var options = new DbContextOptionsBuilder(new DbContextOptions<DbContext>(extensions))
                            .ConfigureTestWarnings()
                            .Options;

                        _options[engine] = options;
                        await using var schema = new BinaryIdentityContext(options);
                        await schema.Database.EnsureCreatedAsync(CancellationToken.None);
                    });

                _databases.Add(engine, database);
            }

            var builder = new DbContextOptionsBuilder(_options[engine]).ConfigureTestWarnings();

            if (interceptors.Length != 0)
            {
                builder.AddInterceptors(interceptors);
            }

            var context = new BinaryIdentityContext(builder.Options);

            try
            {
                // WHY: Immediate self-FK checks require removing parent links before clearing this model's nodes.
                await context
                    .Set<BinaryTreeNode>()
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(node => node.ParentId, (int?)null),
                        CancellationToken.None);

                await context
                    .Set<BinaryTreeNode>()
                    .ExecuteDeleteAsync(CancellationToken.None);

                var registry = context
                    .Model
                    .GetEntityTypes()
                    .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

                await context
                    .Set<NestedSetTreeRegistry>(registry.Name)
                    .ExecuteDeleteAsync(CancellationToken.None);

                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (var database in _databases.Values)
            {
                await database.DisposeAsync();
            }

            await _keyFixture.DisposeAsync();
        }
    }

    /// <summary>Maps a bounded native binary TreeId without an additional application parent entity.</summary>
    private sealed class BinaryIdentityContext : DbContext
    {
        /// <summary>Uses the fixture's provider configuration and independently named hierarchy tables.</summary>
        internal BinaryIdentityContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryTreeNode>();
            node.ToTable("MutableBinaryIdentityNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.TreeId)
                .HasMaxLength(8);
            node
                .Property(value => value.Payload)
                .HasMaxLength(80);
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));
        }
    }

    /// <summary>Uses a mutable binary tree identity with an ordinary integer node key and optional scope.</summary>
    private sealed class BinaryTreeNode : IScopedNestedSetNode<int, byte[], int>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public byte[] TreeId { get; set; } = [];

        /// <inheritdoc />
        public int Scope { get; set; }

        /// <summary>Gets or sets the optional direct parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets application payload independent of structural coordinates.</summary>
        public string Payload { get; set; } = "original";

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Pauses the first source-resolution SELECT before the provider reads any result.</summary>
    private sealed class SourceResolutionBarrier : DbCommandInterceptor
    {
        private int _armed;
        private int _observed;

        /// <summary>Signals that asynchronous source resolution reached the database boundary.</summary>
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Releases the source-resolution command after caller mutation.</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Activates the barrier after setup inserts completed.</summary>
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Volatile.Read(ref _armed) != 0
                && command
                    .CommandText
                    .TrimStart()
                    .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("MutableBinaryIdentityNodes", StringComparison.Ordinal)
                && command.CommandText.Contains(nameof(BinaryTreeNode.TreeId), StringComparison.Ordinal)
                && Interlocked.Exchange(ref _observed, 1) == 0)
            {
                Attempted.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>Pauses after binary-key identities were selected, before any exact-tree mutation begins.</summary>
    private sealed class BinaryKeyResolutionBarrier : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];
        private int _armed;
        private int _observed;

        /// <summary>Signals that the original key-based database lookup executed.</summary>
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Allows materialization and the subsequent mutation to continue.</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Activates the barrier after the hierarchy has been arranged.</summary>
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);

        /// <summary>Reports an early mutation failure or unmatched SQL without hiding it behind a timeout.</summary>
        internal async Task WaitForResolutionAsync(
            Task operation,
            CancellationToken cancellationToken
        )
        {
            // WHY: The mutation can fail before a reader executes. Waiting only for Attempted would replace
            // that provider error with an unrelated timeout and hide the actual command and failure boundary.
            var completed = await Task
                .WhenAny(Attempted.Task, operation)
                .WaitAsync(cancellationToken);

            if (ReferenceEquals(completed, operation))
            {
                throw new InvalidOperationException(
                    "The mutation completed before the identity-resolution barrier. Observed SQL: "
                    + string.Join(Environment.NewLine, _commands),
                    operation.Exception);
            }
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Volatile.Read(ref _armed) != 0)
            {
                _commands.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (Volatile.Read(ref _armed) != 0
                && MatchesIdentityResolution(command, eventData)
                && Interlocked.Exchange(ref _observed, 1) == 0)
            {
                Attempted.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        /// <summary>Matches physical hierarchy identifiers using the executing context's provider metadata.</summary>
        private static bool MatchesIdentityResolution(
            DbCommand command,
            CommandEventData eventData
        )
        {
            if (!command
                    .CommandText
                    .TrimStart()
                    .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var context = eventData.Context;
            Assert.NotNull(context);
            var hierarchy = context.Model.FindEntityType(typeof(BinaryNode));
            Assert.NotNull(hierarchy);
            var tableName = hierarchy.GetTableName();
            Assert.NotNull(tableName);
            var table = StoreObjectIdentifier.Table(tableName, hierarchy.GetSchema());

            var treeId = hierarchy.FindProperty(nameof(BinaryNode.TreeId));
            Assert.NotNull(treeId);
            var columnName = treeId.GetColumnName(table);
            Assert.NotNull(columnName);
            var sql = context.GetService<ISqlGenerationHelper>();

            // WHY: PostgreSQL's fixture maps TreeId to treeId, while a rowset can additionally project a
            // TreeId alias. Match the stored identifiers so scalar and rowset queries reach the same barrier.

            return command.CommandText.Contains(
                    sql.DelimitIdentifier(table.Name, table.Schema),
                    StringComparison.Ordinal)
                && command.CommandText.Contains(sql.DelimitIdentifier(columnName), StringComparison.Ordinal);
        }
    }
}
