namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class ConfiguredMutableIdentityTests
{
    /// <summary>Stores every caller-visible structural representation for exact rollback comparison.</summary>
    private sealed record IdentitySnapshot(
        string Id,
        string Scope,
        string TreeId,
        string? Parent,
        long Left,
        long Right,
        int Depth,
        long Position
    );

    /// <summary>Stores the observable lifecycle state of a configured mutable tree identity.</summary>
    private sealed record RegistrySnapshot(
        string TreeId,
        long Revision,
        byte Lifecycle
    );

    /// <summary>Owns only this model's databases and reuses the established provider resource owner.</summary>
    public sealed class ConfiguredIdentityFixture : IAsyncLifetime
    {
        private readonly Dictionary<string, TestDatabase> _databases = new(StringComparer.Ordinal);
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
        }

        /// <summary>Returns this fixture's model with the requested asynchronous database barrier.</summary>
        /// <param name="cancellationToken">The caller's token for cancellable fixture work.</param>
        internal async Task<DbContext> CreateContextAsync(
            string engine,
            CancellationToken cancellationToken,
            params IInterceptor[] interceptors
        )
        {
            if (!_databases.TryGetValue(engine, out var database))
            {
                // WHY: TestDatabase shares assembly-owned servers but allocates an isolated database for our model.
                // This adds no containers and does not alter another fixture's tables or resource lifecycle.
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
                        await using var schema = new ConfiguredIdentityContext(options);
                        await schema.Database.EnsureCreatedAsync(token);
                    },
                    cancellationToken: cancellationToken);

                _databases.Add(engine, database);
            }

            var builder = new DbContextOptionsBuilder(_options[engine]).ConfigureTestWarnings();
            builder.AddInterceptors(interceptors);

            var context = new ConfiguredIdentityContext(builder.Options);

            // WHY: The additional callback cases reuse this model's database. A complete fixture reset must
            // remove restrictive parent links, payload and registry lifecycle state before another isolated case.
            await context
                .Set<ConfiguredIdentityNode>()
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(node => node.ParentId, (MutableIdentity?)null),
                    cancellationToken);

            await context
                .Set<ConfiguredIdentityNode>()
                .ExecuteDeleteAsync(cancellationToken);

            await context.ClearNestedSetTreeRegistriesAsync(cancellationToken);

            return context;
        }
    }

    /// <summary>
    /// Converts every identity role to a bounded native string with an explicit deep model snapshot.
    /// </summary>
    private sealed class ConfiguredIdentityContext : DbContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public ConfiguredIdentityContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<ConfiguredIdentityNode>();
            node.ToTable("ConfiguredMutableIdentityNodes");
            node.HasKey(value => value.Id);

            foreach (var propertyName in new[]
                {
                    nameof(ConfiguredIdentityNode.Id),
                    nameof(ConfiguredIdentityNode.Scope),
                    nameof(ConfiguredIdentityNode.TreeId),
                    nameof(ConfiguredIdentityNode.ParentId),
                })
            {
                var property = node.Property<MutableIdentity>(propertyName);
                property.HasMaxLength(40);
                property.HasConversion(value => value.Value, value => new MutableIdentity(value));
                property.Metadata.SetValueComparer(
                    new ValueComparer<MutableIdentity>(
                        (left, right) => left != null && right != null && left.Value == right.Value,
                        value => StringComparer.Ordinal.GetHashCode(value.Value),
                        value => new MutableIdentity(value.Value)));
            }

            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Scope)
                .IsRequired();

            node
                .Property(value => value.TreeId)
                .IsRequired();

            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));
        }
    }

    /// <summary>
    /// Uses a converted mutable node key, scope, and tree identity on one ordinary hierarchy entity.
    /// </summary>
    private sealed class
        ConfiguredIdentityNode : IScopedNestedSetNode<MutableIdentity, MutableIdentity, MutableIdentity>
    {
        /// <summary>Gets or sets the nullable direct parent key.</summary>
        public MutableIdentity? ParentId { get; set; }

        /// <inheritdoc />
        public MutableIdentity Id { get; set; } = new(string.Empty);

        /// <inheritdoc />
        public MutableIdentity Scope { get; set; } = new(string.Empty);

        /// <inheritdoc />
        public MutableIdentity TreeId { get; set; } = new(string.Empty);

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Represents a caller-mutable model value converted through its string representation.</summary>
    private sealed class MutableIdentity
    {
        /// <summary>Retains the supplied fixture values without changing their identity.</summary>
        /// <param name="value">The model value retained by this fixture identity.</param>
        public MutableIdentity(
            string value
        )
        {
            Value = value;
        }

        /// <summary>Gets or sets the application-owned identity representation.</summary>
        public string Value { get; set; }
    }

    /// <summary>
    /// Pauses after native lookup parameters were consumed and before source materialization completes.
    /// </summary>
    private sealed class IdentityResolutionBarrier : DbCommandInterceptor
    {
        private int _armed;
        private int _observed;

        /// <summary>Signals that the operation reached its asynchronous source lookup boundary.</summary>
        internal TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Allows source materialization and the typed mutation to continue after caller edits.</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Activates the one-shot barrier after arranging the persisted hierarchy.</summary>
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);

        /// <inheritdoc />
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (Volatile.Read(ref _armed) != 0
                && command
                    .CommandText
                    .TrimStart()
                    .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("ConfiguredMutableIdentityNodes", StringComparison.Ordinal)
                && command.CommandText.Contains(nameof(ConfiguredIdentityNode.TreeId), StringComparison.Ordinal)
                && Interlocked.Exchange(ref _observed, 1) == 0)
            {
                // WHY: Consumed SQL parameters prove the lookup used the original caller identities. Mutating at
                // this barrier isolates whether the subsequent bound operation owns its configured snapshots.
                Attempted.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
