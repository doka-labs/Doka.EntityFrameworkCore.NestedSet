namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Verifies that configured mutable identities remain owned across asynchronous public mutation boundaries.
/// </summary>
public abstract partial class ConfiguredMutableIdentityTests : ProviderTest
{
    private readonly ConfiguredIdentityFixture _fixture;

    /// <summary>Creates tests using isolated databases on the existing assembly-owned provider servers.</summary>
    protected ConfiguredMutableIdentityTests(
        IProviderFixture<ConfiguredIdentityFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    /// Caller edits cannot redirect the scope, source key, or target tree while source lookup awaits.
    /// </summary>
    [Fact]
    public async Task DetachmentOwnsConfiguredScopeNodeKeyAndTreeIdentityAcrossAwait()
    {
        // Arrange
        var barrier = new IdentityResolutionBarrier();
        await using var context = await _fixture.CreateContextAsync(Engine, barrier);
        var scope = new MutableIdentity("scope");
        var hierarchy = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope);

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("root") },
            new MutableIdentity("source"),
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("child") },
            new MutableIdentity("root"),
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("other") },
            new MutableIdentity("root"),
            CancellationToken.None);

        var nodeKey = new MutableIdentity("child");
        var targetTree = new MutableIdentity("target");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        barrier.Arm();

        // Act
        var detachment = hierarchy.DetachAsTreeAsync(nodeKey, targetTree, timeout.Token);
        await barrier.Attempted.Task.WaitAsync(timeout.Token);

        try
        {
            scope.Value = "redirected-scope";
            nodeKey.Value = "other";
            targetTree.Value = "redirected-tree";
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await detachment;
        var detached = await hierarchy
            .InTree(new MutableIdentity("target"))
            .Nodes
            .SingleAsync(timeout.Token);

        var retained = await hierarchy
            .InTree(new MutableIdentity("source"))
            .Nodes
            .ToArrayAsync(timeout.Token);

        var redirectedTreeExists = await hierarchy
            .InTree(targetTree)
            .Nodes
            .AnyAsync(timeout.Token);

        var redirectedScopeExists = await context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(scope)
            .TreeContaining(new MutableIdentity("child"))
            .AnyAsync(timeout.Token);

        // Assert
        Assert.Equal(
            ("child", "scope", "target", 1L, 2L, 0),
            (detached.Id.Value, detached.Scope.Value, detached.TreeId.Value, detached.Left, detached.Right,
                detached.Depth));
        Assert.Null(detached.ParentId);
        Assert.Collection(
            retained,
            root => Assert.Equal(("root", 1L, 4L, 0), (root.Id.Value, root.Left, root.Right, root.Depth)),
            child => Assert.Equal(
                ("other", "root", 2L, 3L, 1),
                (child.Id.Value, child.ParentId!.Value, child.Left, child.Right, child.Depth)));
        Assert.All(retained, node => Assert.Equal(("scope", "source"), (node.Scope.Value, node.TreeId.Value)));
        Assert.False(redirectedTreeExists);
        Assert.False(redirectedScopeExists);
    }

    /// <summary>Pairs every provider and insertion path with callbacks before and after payload persistence.</summary>
    public static IEnumerable<TheoryDataRow<bool, bool>> CallbackCases()
    {
        yield return new TheoryDataRow<bool, bool>(false, false);
        yield return new TheoryDataRow<bool, bool>(false, true);
        yield return new TheoryDataRow<bool, bool>(true, false);
        yield return new TheoryDataRow<bool, bool>(true, true);
    }

    /// <summary>A callback cannot mutate a configured parent snapshot shared with a staged insertion.</summary>
    /// <param name="bulk">Whether the insertion uses the branch import path.</param>
    /// <param name="afterSave">Whether the callback runs after the payload INSERT completed.</param>
    /// <returns>A task that completes after verifying exact input and database rollback.</returns>
    [Theory]
    [MemberData(nameof(CallbackCases))]
    public async Task CallbackParentMutationRestoresConfiguredInput(
        bool bulk,
        bool afterSave
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine);
        var hierarchy = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(new MutableIdentity("scope"));

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("root") },
            new MutableIdentity("source"),
            CancellationToken.None);

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("other") },
            new MutableIdentity("other-tree"),
            CancellationToken.None);

        var input = OriginalInput();
        var original = Snapshot(input);
        var rowsBefore = await RowsAsync(context);
        var registriesBefore = await RegistriesAsync(context);
        var callbacks = 0;
        ObserveSave(
            context,
            afterSave,
            () =>
            {
                callbacks++;
                input.ParentId!.Value = "other";
            });

        // Act
        var error = await Record.ExceptionAsync(() => InsertAsync(hierarchy, input, bulk));

        // Assert
        Assert.Equal(
            bulk ? NestedSetErrorCode.InvalidImport : NestedSetErrorCode.InvalidStructure,
            Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(1, callbacks);
        Assert.Equal(original, Snapshot(input));
        Assert.Equal(rowsBefore, await RowsAsync(context));
        Assert.Equal(registriesBefore, await RegistriesAsync(context));
        Assert.Equal(EntityState.Detached, context.Entry(input).State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Replacing a parent object with the same converted identity remains a valid callback write.</summary>
    /// <param name="bulk">Whether the insertion uses the branch import path.</param>
    /// <param name="afterSave">Whether the callback runs after the payload INSERT completed.</param>
    /// <returns>A task that completes after verifying the configured parent and exact tree remain valid.</returns>
    [Theory]
    [MemberData(nameof(CallbackCases))]
    public async Task CallbackMayReplaceParentWithProviderIdenticalValue(
        bool bulk,
        bool afterSave
    )
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync(Engine);
        var hierarchy = context
            .NestedSet<ConfiguredIdentityNode>()
            .ForScope(new MutableIdentity("scope"));

        await hierarchy.InsertRootAsync(
            new ConfiguredIdentityNode { Id = new MutableIdentity("root") },
            new MutableIdentity("source"),
            CancellationToken.None);

        var input = OriginalInput();
        MutableIdentity? assignedParent = null;
        MutableIdentity? replacementParent = null;
        ObserveSave(
            context,
            afterSave,
            () =>
            {
                assignedParent = input.ParentId;
                replacementParent = new MutableIdentity(input.ParentId!.Value);
                input.ParentId = replacementParent;
            });

        // Act
        await InsertAsync(hierarchy, input, bulk);

        // Assert
        Assert.NotNull(assignedParent);
        Assert.NotNull(replacementParent);
        Assert.NotSame(assignedParent, replacementParent);
        Assert.Equal(
            ("input", "scope", "source", "root", 2L, 3L, 1, 0L),
            (input.Id.Value, input.Scope.Value, input.TreeId.Value, input.ParentId!.Value, input.Left, input.Right,
                input.Depth, input.Position));
        Assert.Equal(
            "root",
            (await hierarchy
                .ParentOf(new MutableIdentity("input"))
                .SingleAsync(CancellationToken.None)).Id.Value);
        Assert.True(
            (await hierarchy
                .InTree(new MutableIdentity("source"))
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Dispatches one public insertion while retaining the configured mutable parent model.</summary>
    private static Task InsertAsync(
        ScopedNestedSet<ConfiguredIdentityNode, MutableIdentity> hierarchy,
        ConfiguredIdentityNode input,
        bool bulk
    ) => bulk
        ? hierarchy.InsertSubtreeAsync(
            new NestedSetBranch<ConfiguredIdentityNode>(input),
            new MutableIdentity("root"),
            CancellationToken.None)
        : hierarchy.InsertChildAsync(input, new MutableIdentity("root"), CancellationToken.None);

    /// <summary>Registers one application callback at the selected active stage after fixture seeding.</summary>
    private static void ObserveSave(
        DbContext context,
        bool afterSave,
        Action callback
    )
    {
        if (afterSave)
        {
            context.SavedChanges += (_, _) => callback();
        }
        else
        {
            context.SavingChanges += (_, _) => callback();
        }
    }

    /// <summary>Creates distinctive caller values whose exact restoration cannot be mistaken for defaults.</summary>
    private static ConfiguredIdentityNode OriginalInput() => new()
    {
        Id = new MutableIdentity("input"),
        Scope = new MutableIdentity("original-scope"),
        TreeId = new MutableIdentity("original-tree"),
        ParentId = new MutableIdentity("original-parent"),
        Left = 71,
        Right = 72,
        Depth = 17,
        Position = 23,
    };

    /// <summary>Captures mutable representations by value rather than retaining caller-owned objects.</summary>
    private static IdentitySnapshot Snapshot(
        ConfiguredIdentityNode node
    ) => new(
        node.Id.Value,
        node.Scope.Value,
        node.TreeId.Value,
        node.ParentId?.Value,
        node.Left,
        node.Right,
        node.Depth,
        node.Position);

    /// <summary>Reads exact persisted structure independently of the operation's tracker state.</summary>
    private static async Task<IdentitySnapshot[]> RowsAsync(
        DbContext context
    )
    {
        var rows = await context
            .Set<ConfiguredIdentityNode>()
            .AsNoTracking()
            .ToArrayAsync(CancellationToken.None);

        return rows
            .OrderBy(row => row.Id.Value, StringComparer.Ordinal)
            .Select(Snapshot)
            .ToArray();
    }

    /// <summary>Captures registry identity, revision and lifecycle to detect hidden callback writes.</summary>
    private static async Task<RegistrySnapshot[]> RegistriesAsync(
        DbContext context
    )
    {
        var entityType = context.Model.FindEntityType(typeof(ConfiguredIdentityNode))!;
        var registry = NestedSetTreeRegistryMapping.For(entityType).Registry;
        var rows = await context
            .Set<NestedSetTreeRegistry>(registry.Name)
            .AsNoTracking()
            .Select(row => new
            {
                TreeId = EF.Property<MutableIdentity>(row, NestedSetTreeRegistryMetadata.TreeId),
                Revision = EF.Property<long>(row, NestedSetTreeRegistryMetadata.Revision),
                Lifecycle = EF.Property<byte>(row, NestedSetTreeRegistryMetadata.Lifecycle),
            })
            .ToArrayAsync(CancellationToken.None);

        return rows
            .OrderBy(row => row.TreeId.Value, StringComparer.Ordinal)
            .Select(row => new RegistrySnapshot(row.TreeId.Value, row.Revision, row.Lifecycle))
            .ToArray();
    }

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

        /// <summary>Returns this fixture's model with the requested asynchronous database barrier.</summary>
        internal async Task<DbContext> CreateContextAsync(
            string engine,
            params IInterceptor[] interceptors
        )
        {
            if (!_databases.TryGetValue(engine, out var database))
            {
                // WHY: TestDatabase shares assembly-owned servers but allocates an isolated database for our model.
                // This adds no containers and does not alter another fixture's tables or resource lifecycle.
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
                        await using var schema = new ConfiguredIdentityContext(options);
                        await schema.Database.EnsureCreatedAsync(CancellationToken.None);
                    });

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
                    CancellationToken.None);

            await context
                .Set<ConfiguredIdentityNode>()
                .ExecuteDeleteAsync(CancellationToken.None);

            await context.ClearNestedSetTreeRegistriesAsync(CancellationToken.None);

            return context;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (var database in _databases.Values)
            {
                await database.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Converts every identity role to a bounded native string with an explicit deep model snapshot.
    /// </summary>
    private sealed class ConfiguredIdentityContext(DbContextOptions options) : DbContext(options)
    {
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
        /// <inheritdoc />
        public MutableIdentity Id { get; set; } = new(string.Empty);

        /// <inheritdoc />
        public MutableIdentity Scope { get; set; } = new(string.Empty);

        /// <inheritdoc />
        public MutableIdentity TreeId { get; set; } = new(string.Empty);

        /// <summary>Gets or sets the nullable direct parent key.</summary>
        public MutableIdentity? ParentId { get; set; }

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
    private sealed class MutableIdentity(string value)
    {
        /// <summary>Gets or sets the application-owned identity representation.</summary>
        public string Value { get; set; } = value;
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
