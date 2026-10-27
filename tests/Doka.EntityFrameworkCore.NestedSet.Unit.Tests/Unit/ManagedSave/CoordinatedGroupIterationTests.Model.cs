namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CoordinatedGroupIterationTests
{
    /// <summary>Creates two independent owner mappings that share one physical SQLite transaction boundary.</summary>
    private static async Task<GroupContext> CreateContextAsync()
    {
        var context = new GroupContext();

        try
        {
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            await context.Database.EnsureCreatedAsync(CancellationToken.None);
            await SeedAsync<FirstOwner>(context);
            await SeedAsync<SecondOwner>(context);
            context.ChangeTracker.Clear();

            return context;
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    /// <summary>Seeds sibling order opposite to insertion order through the actual hierarchy facade.</summary>
    private static async Task SeedAsync<TOwner>(
        GroupContext context
    )
    {
        var hierarchy = context.NestedSet<GroupNode<TOwner>>().ForScope(1);
        await hierarchy.InsertRootAsync(
            new GroupNode<TOwner> { Id = 1, Scope = 1, Name = "Root" },
            Guid.NewGuid(),
            CancellationToken.None);
        await hierarchy.InsertChildAsync(
            new GroupNode<TOwner> { Id = 2, Scope = 1, Name = "Bravo" },
            1,
            CancellationToken.None);
        await hierarchy.InsertChildAsync(
            new GroupNode<TOwner> { Id = 3, Scope = 1, Name = "Alpha" },
            1,
            CancellationToken.None);
    }

    /// <summary>Coordinates two distinct entity types without EF inheritance between their owners.</summary>
    private sealed class GroupContext : DbContext
    {
        /// <summary>Creates an isolated connection whose lifetime matches this one test context.</summary>
        internal GroupContext() : base(new DbContextOptionsBuilder<GroupContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options) { }

        /// <summary>Gets or sets whether the underlying payload save fails after writing both owner rows.</summary>
        internal bool FailAfterPayload { get; set; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            Configure<FirstOwner>(modelBuilder, "FirstOwnerNodes");
            Configure<SecondOwner>(modelBuilder, "SecondOwnerNodes");
        }

        /// <inheritdoc />
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        ) => this.SaveNestedSetChangesAsync(
            acceptAllChangesOnSuccess,
            SavePayloadAsync,
            cancellationToken);

        /// <summary>Fails after payload SQL so the coordinator's finally and transaction cleanup execute.</summary>
        private async Task<int> SavePayloadAsync(
            CancellationToken cancellationToken
        )
        {
            // WHY: This delegate calls DbContext directly, retains pending changes and fails only after SQL;
            // the coordinator still owns acceptance, parent restoration and transaction cleanup.
            var saved = await base.SaveChangesAsync(false, cancellationToken);

            if (FailAfterPayload)
            {
                throw new PayloadFailureException();
            }

            return saved;
        }

        /// <summary>Maps one exact hierarchy owner and its own registry and foreign key.</summary>
        private static void Configure<TOwner>(
            ModelBuilder modelBuilder,
            string table
        )
        {
            var entity = modelBuilder.Entity<GroupNode<TOwner>>();
            entity.ToTable(table);
            entity.Property(node => node.Id).ValueGeneratedNever();
            entity.HasNestedSet(builder => builder
                .HasScope(node => node.Scope)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentId)
                .OrderBy(node => node.Name));
        }
    }

    /// <summary>Identifies the first independently mapped hierarchy owner.</summary>
    private sealed class FirstOwner;

    /// <summary>Identifies the second independently mapped hierarchy owner.</summary>
    private sealed class SecondOwner;

    /// <summary>Distinguishes an injected payload failure from a database or hierarchy rejection.</summary>
    private sealed class PayloadFailureException : Exception;

    /// <summary>Provides the same scalar shape as two unrelated hierarchy entity types.</summary>
    private sealed class GroupNode<TOwner> : IScopedNestedSetNode<int, Guid, int>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Scope { get; set; }

        /// <summary>Gets or sets this owner's optional direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the payload value that also determines sibling order.</summary>
        public string Name { get; set; } = string.Empty;

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }
}
