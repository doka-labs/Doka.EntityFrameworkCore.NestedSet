namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class MutableTreeIdentityTests
{
    /// <summary>Owns additional binary-identity tables in independently allocated test databases.</summary>
    public sealed class BinaryIdentityFixture : IAsyncLifetime
    {
        private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);
        private readonly RelationalFixture _keyFixture = new();
        private readonly Dictionary<string, DbContextOptions> _options = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (var database in _databases.Values)
            {
                await database.DisposeAsync();
            }

            await _keyFixture.DisposeAsync();
        }

        /// <summary>Uses the existing binary-node model in a separate fixture-owned database.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        internal async Task<TreeContext> CreateKeyContextAsync(
            string engine,
            CancellationToken cancellationToken,
            params IInterceptor[] interceptors
        )
        {
            var database = await _keyFixture.ResetAsync(engine, cancellationToken: cancellationToken);

            return database.CreateContext(interceptors);
        }

        /// <summary>Returns a reset binary model using the existing provider configuration and lifecycle.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        internal async Task<DbContext> CreateContextAsync(
            string engine,
            CancellationToken cancellationToken,
            params IInterceptor[] interceptors
        )
        {
            if (!_databases.TryGetValue(engine, out var database))
            {
                // WHY: Reuse the existing provider and resource owner with this test's model initializer.
                // Only our tables are created; the assembly-owned container and other classes stay untouched.
                database = await TestDatabase.CreateAsync(
                    engine,
                    async (source, token) =>
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
                        await schema.Database.EnsureCreatedAsync(token);
                    },
                    cancellationToken: cancellationToken);

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
                        cancellationToken);

                await context
                    .Set<BinaryTreeNode>()
                    .ExecuteDeleteAsync(cancellationToken);

                var registry = context
                    .Model
                    .GetEntityTypes()
                    .Single(entity => entity.ClrType == typeof(NestedSetTreeRegistry));

                await context
                    .Set<NestedSetTreeRegistry>(registry.Name)
                    .ExecuteDeleteAsync(cancellationToken);

                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
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
        /// <summary>Gets or sets the optional direct parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets application payload independent of structural coordinates.</summary>
        public string Payload { get; set; } = "original";

        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public byte[] TreeId { get; set; } = [];

        /// <inheritdoc />
        public int Scope { get; set; }

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
