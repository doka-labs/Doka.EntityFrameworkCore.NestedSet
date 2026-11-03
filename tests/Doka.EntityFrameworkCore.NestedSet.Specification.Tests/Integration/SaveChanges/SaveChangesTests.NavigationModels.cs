namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Creates an isolated navigation model through the existing real-provider database lifecycle.</summary>
    private async Task<TestDatabase> CreateNavigationDatabaseAsync()
    {
        var database = await TestDatabase.CreateAsync(Engine);

        try
        {
            await using var context = await CreateNavigationContextAsync(database, wrapped: true);
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(CancellationToken.None);
            var owner = new NavigationScopeOwner { Value = "owner" };
            await context.AddRangeAsync(
                [owner, new NavigationScopeOwner { Value = "other owner" }, new NavigationNote { Id = 1 }],
                CancellationToken.None);

            await context.SaveChangesAsync(CancellationToken.None);
            var hierarchy = context.NestedSet<NavigationNode>().ForScope(owner.Id);
            await hierarchy.InsertRootAsync(new NavigationNode { Id = 1, Name = "Root" }, s_treeId,
                CancellationToken.None);

            await hierarchy.InsertChildAsync(new NavigationNode { Id = 2, Name = "Alpha" }, 1,
                CancellationToken.None);

            await hierarchy.InsertChildAsync(new NavigationNode { Id = 3, Name = "Bravo" }, 1,
                CancellationToken.None);

            return database;
        }
        catch
        {
            // WHY: The caller can only own the database after all model tables and seed rows exist.
            await database.DisposeAsync();

            throw;
        }
    }

    /// <summary>Reuses the selected provider while choosing its coordinated or ordinary save boundary.</summary>
    /// <param name="database">The isolated database owned by this test.</param>
    /// <param name="wrapped">Whether to use NestedSetDbContext's save overrides.</param>
    /// <param name="interceptors">Observers scoped to this context.</param>
    /// <returns>A context whose lifetime belongs to the caller.</returns>
    private static async Task<DbContext> CreateNavigationContextAsync(
        TestDatabase database,
        bool wrapped,
        params IInterceptor[] interceptors
    )
    {
        var options = await SaveSemanticsFixture.OptionsAsync(database, interceptors);

        return wrapped ? new NavigationSaveContext(options) : new NavigationPlainContext(options);
    }

    /// <summary>
    ///     Chooses the exact synchronous or asynchronous application boundary requested by one test case.
    /// </summary>
    private static Task<int> SaveNavigationAsync(DbContext context, bool synchronous)
    {
        if (synchronous)
        {
            // WHY: The synchronous cases must exercise SaveChanges itself, including its native callback sequence.
            // ReSharper disable once MethodHasAsyncOverload
            return Task.FromResult(context.SaveChanges());
        }

        return context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Maps parent and scope navigations independently of the scalar-only ordering fixture.</summary>
    private static void ConfigureNavigationModel(ModelBuilder builder)
    {
        var node = builder.Entity<NavigationNode>();
        node.ToTable("NavigationSaveNodes");
        node.Property(value => value.Id).ValueGeneratedNever();
        node.Property(value => value.Name).HasMaxLength(128);
        node.HasNestedSet(value => value
            .HasTreeId(item => item.TreeId)
            .HasScope(item => item.Scope)
            .HasParent(item => item.ParentId)
            .OrderBy(item => item.Name));

        node.HasOne(value => value.Parent).WithMany(value => value.Children)
            .HasForeignKey(value => new { value.Scope, value.ParentId })
            .HasPrincipalKey(value => new { value.Scope, value.Id })
            .OnDelete(DeleteBehavior.Restrict);

        node.HasOne(value => value.ScopeOwner).WithMany(value => value.Nodes)
            .HasForeignKey(value => value.Scope).OnDelete(DeleteBehavior.Cascade);

        node.HasOne(value => value.PayloadOwner).WithMany(value => value.Nodes)
            .HasForeignKey(value => new { value.Scope, value.PayloadOwnerId })
            .HasPrincipalKey(value => new { value.Scope, value.Id })
            .OnDelete(DeleteBehavior.Restrict);

        var owner = builder.Entity<NavigationScopeOwner>();
        owner.ToTable("NavigationSaveOwners");
        owner.Property(value => value.Value).HasMaxLength(128);
        var payloadOwner = builder.Entity<NavigationPayloadOwner>();
        payloadOwner.ToTable("NavigationSavePayloadOwners");
        payloadOwner.HasKey(value => new { value.Scope, value.Id });
        payloadOwner.Property(value => value.Id).ValueGeneratedNever();
        var note = builder.Entity<NavigationNote>();
        note.ToTable("NavigationSaveNotes");
        note.Property(value => value.Id).ValueGeneratedNever();
        note.Property(value => value.Value).HasMaxLength(128);
    }

    /// <summary>Coordinates hierarchy work while preserving ordinary manually detected payload.</summary>
    private sealed class NavigationSaveContext : NestedSetDbContext
    {
        /// <summary>Creates the coordinated context from the established provider options.</summary>
        internal NavigationSaveContext(DbContextOptions options) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureNavigationModel(modelBuilder);
    }

    /// <summary>Exercises provider registration without supplying a coordinated save override.</summary>
    private sealed class NavigationPlainContext : DbContext
    {
        /// <summary>Creates the ordinary context from the established provider options.</summary>
        internal NavigationPlainContext(DbContextOptions options) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureNavigationModel(modelBuilder);
    }

    /// <summary>Contains hierarchy-owned scalars and the relationships whose snapshots are guarded.</summary>
    private sealed class NavigationNode : IScopedNestedSetNode<int, Guid, int>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Scope { get; set; }

        /// <inheritdoc />
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the configured sibling order.</summary>
        public string Name { get; set; } = string.Empty;

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }

        /// <summary>Gets or sets the direct parent without requiring an explicitly assigned FK.</summary>
        public NavigationNode? Parent { get; set; }

        /// <summary>Gets the collection whose EF relationship snapshot can retain detached entries.</summary>
        public List<NavigationNode> Children { get; } = [];

        /// <summary>Gets or sets the ordinary principal supplying the hierarchy scope key.</summary>
        public NavigationScopeOwner? ScopeOwner { get; set; }

        /// <summary>Gets or sets the ordinary nullable part of a foreign key that overlaps the guarded scope.</summary>
        public int? PayloadOwnerId { get; set; }

        /// <summary>Gets or sets an ordinary principal whose identity includes the existing hierarchy scope.</summary>
        public NavigationPayloadOwner? PayloadOwner { get; set; }
    }

    /// <summary>Provides a store-generated principal key and a cascade relationship to hierarchy nodes.</summary>
    private sealed class NavigationScopeOwner
    {
        /// <summary>Gets or sets the generated scope identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets ordinary principal payload.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Gets the hierarchy members in this application scope.</summary>
        public List<NavigationNode> Nodes { get; } = [];
    }

    /// <summary>Provides an ordinary relationship whose composite FK overlaps the hierarchy-owned scope.</summary>
    private sealed class NavigationPayloadOwner
    {
        /// <summary>Gets or sets the first component of the ordinary principal identity.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the explicit identity within the application scope.</summary>
        public int Id { get; set; }

        /// <summary>Gets the related nodes without making the ordinary relationship a hierarchy role.</summary>
        public List<NavigationNode> Nodes { get; } = [];
    }

    /// <summary>Requires the exact library or EF immutable-key rejection produced by this mapped scope.</summary>
    private static void AssertScopeChangeRejected(
        Exception? failure,
        DbContext context,
        bool wrapped,
        bool synchronous,
        bool automaticDetection
    )
    {
        if (wrapped && !synchronous && automaticDetection)
        {
            // WHY: Scope also identifies self-FK principals, so coordinated detection reaches EF's immutable key.
            var rejection = Assert.IsType<InvalidOperationException>(failure);
            var metadata = context.Model.FindEntityType(typeof(NavigationNode))!;
            Assert.Equal(CoreStrings.KeyReadOnly(nameof(NavigationNode.Scope), metadata.DisplayName()),
                rejection.Message);
        }
        else
        {
            Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        }
    }

    /// <summary>Represents ordinary application payload independent of the hierarchy.</summary>
    private sealed class NavigationNote
    {
        /// <summary>Gets or sets the explicit application key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the payload whose automatic detection policy must be retained.</summary>
        public string Value { get; set; } = "original";
    }

    /// <summary>Observes all writes to this navigation model without changing provider behavior.</summary>
    private sealed class NavigationWriteProbe : DbCommandInterceptor
    {
        /// <summary>Gets the number of application INSERT, UPDATE or DELETE commands.</summary>
        internal int Writes { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Observe(command);

            return result;
        }

        /// <inheritdoc />
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            Observe(command);

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command);

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command);

            return ValueTask.FromResult(result);
        }

        /// <summary>Counts model writes independently of provider-specific RETURNING clauses.</summary>
        private void Observe(DbCommand command)
        {
            if (command.CommandText.Contains("NavigationSave", StringComparison.Ordinal)
                && (command.CommandText.Contains("INSERT ", StringComparison.OrdinalIgnoreCase)
                    || command.CommandText.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
                    || command.CommandText.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)))
            {
                Writes++;
            }
        }
    }
}
