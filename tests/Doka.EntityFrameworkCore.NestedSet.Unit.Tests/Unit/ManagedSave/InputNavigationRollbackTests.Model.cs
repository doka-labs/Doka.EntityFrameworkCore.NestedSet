namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InputNavigationRollbackTests
{
    /// <summary>Provides real hierarchy and owned-payload metadata without opening a database connection.</summary>
    private sealed class RollbackContext : DbContext
    {
        /// <summary>Keeps all lifecycle transitions explicit during staging and rollback.</summary>
        internal RollbackContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<RollbackOwner>()
                .Property(value => value.Id)
                .ValueGeneratedNever();
            var node = modelBuilder.Entity<RollbackNode>();
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId));
            node
                .HasOne(value => value.Parent)
                .WithMany(value => value.Children)
                .HasForeignKey(value => value.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            node.OwnsOne(value => value.Payload,
                payload =>
                {
                    payload.WithOwner(value => value.Root);
                    // WHY: An owned payload may reference an ordinary principal, but that principal cannot
                    // expose an inverse collection of the owned type. This is a valid unidirectional EF mapping.
                    payload
                        .HasOne(value => value.Owner)
                        .WithMany()
                        .HasForeignKey(value => value.OwnerId)
                        .OnDelete(DeleteBehavior.Restrict);
                });
        }
    }

    /// <summary>Retains caller-owned nullable input collections and an optional owned payload.</summary>
    private sealed class RollbackNode : INestedSetNode<int, Guid>
    {
        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional direct-parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the non-owned parent reference initialized only by actual EF fixup.</summary>
        public RollbackNode? Parent { get; set; }

        /// <summary>Gets or sets the caller's nullable non-owned collection storage.</summary>
        public List<RollbackNode>? Children { get; set; }

        /// <summary>Gets or sets the owned payload whose foreign references have separate validation.</summary>
        public RollbackPayload? Payload { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Distinguishes owned graph membership from ordinary, caller-owned payload references.</summary>
    private sealed class RollbackPayload
    {
        /// <summary>Gets or sets the ownership back-reference.</summary>
        public RollbackNode? Root { get; set; }

        /// <summary>Gets or sets the ordinary FK whose business value is never reset by rollback.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Gets or sets the non-owned reference rejected when populated before graph validation.</summary>
        public RollbackOwner? Owner { get; set; }

        /// <summary>Gets or sets ordinary application payload preserved by both cleanup paths.</summary>
        public string Value { get; set; } = "Caller payload";
    }

    /// <summary>Provides an unrelated tracked principal for native owned-payload FK fixup.</summary>
    private sealed class RollbackOwner
    {
        /// <summary>Gets or sets the fixed principal identity.</summary>
        public int Id { get; set; }
    }
}
