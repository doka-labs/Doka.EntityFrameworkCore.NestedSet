namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class SupplementalRelationshipRollbackTests
{
    /// <summary>Provides real EF snapshots for an ordinary nullable inverse collection.</summary>
    private class SupplementalContext : DbContext
    {
        /// <summary>Keeps native fixup distinct from manual raw graph edits.</summary>
        internal SupplementalContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<Owner>()
                .Property(owner => owner.Id)
                .ValueGeneratedNever();

            modelBuilder
                .Entity<Node>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            modelBuilder
                .Entity<Node>()
                .HasOne(node => node.Owner)
                .WithMany(owner => owner.Nodes)
                .HasForeignKey(node => node.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Uses EF's required relationship/orphan semantics with an independently nullable CLR member.</summary>
    private sealed class RequiredContext : SupplementalContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<Node>()
                .Property(node => node.OwnerId)
                .IsRequired();

            modelBuilder
                .Entity<Node>()
                .HasOne(node => node.Owner)
                .WithMany(owner => owner.Nodes)
                .HasForeignKey(node => node.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Separates nullable CLR collection storage from its native membership set.</summary>
    private sealed class Owner
    {
        /// <summary>Gets or sets the fixed ordinary principal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets nullable caller-owned collection storage.</summary>
        public List<Node>? Nodes { get; set; }
    }

    /// <summary>Supplies real ordinary FK fixup without hierarchy or database work.</summary>
    private sealed class Node
    {
        /// <summary>Gets or sets the fixed dependent key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the nullable ordinary principal key.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Gets or sets the ordinary principal reference.</summary>
        public Owner? Owner { get; set; }
    }
}
