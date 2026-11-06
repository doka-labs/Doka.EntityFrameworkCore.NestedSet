namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class UnchangedForeignKeyRollbackTests
{
    /// <summary>Uses actual hierarchy metadata and ordinary nullable FKs without opening SQLite.</summary>
    private class RollbackContext : DbContext
    {
        /// <summary>Keeps raw callback edits distinct from the native identities installed by public setters.</summary>
        internal RollbackContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <summary>Gets whether two ordinary FKs share a non-key scope member on the dependent.</summary>
        protected virtual bool CompositeOwner => false;

        /// <summary>Gets whether the ordinary owner relationship is unique instead of a collection.</summary>
        protected virtual bool UniqueOwner => false;

        /// <summary>Gets whether ordinary principal identities use EF's actual temporary generator.</summary>
        protected virtual bool GeneratedOwner => false;

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
            var owner = modelBuilder.Entity<RollbackOwner>();

            if (CompositeOwner)
            {
                owner.HasKey(value => new
                {
                    value.Scope,
                    value.Id,
                });
            }

            if (GeneratedOwner)
            {
                owner
                    .Property(value => value.Id)
                    .ValueGeneratedOnAdd();
            }
            else
            {
                owner
                    .Property(value => value.Id)
                    .ValueGeneratedNever();
            }

            var node = modelBuilder.Entity<RollbackNode>();
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .OrderBy(value => value.Name));

            var reference = node.HasOne(value => value.Owner);

            if (UniqueOwner)
            {
                owner.Ignore(value => value.Nodes);
                owner.Ignore(value => value.ReviewNodes);
                node.Ignore(value => value.Reviewer);
                node.Ignore(value => value.ReviewerId);
                reference
                    .WithOne(value => value.UniqueNode)
                    .HasForeignKey<RollbackNode>(value => new
                    {
                        value.OwnerScope,
                        value.OwnerId,
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                owner.Ignore(value => value.UniqueNode);

                if (CompositeOwner)
                {
                    reference
                        .WithMany(value => value.Nodes)
                        .HasForeignKey(value => new
                        {
                            value.OwnerScope,
                            value.OwnerId,
                        })
                        .OnDelete(DeleteBehavior.Restrict);

                    // WHY: The shared scope is ordinary non-key payload on this hierarchy node. Repairing one
                    // FK component must also preserve the other installed tuple and its independent reference.
                    node
                        .HasOne(value => value.Reviewer)
                        .WithMany(value => value.ReviewNodes)
                        .HasForeignKey(value => new
                        {
                            value.OwnerScope,
                            value.ReviewerId,
                        })
                        .OnDelete(DeleteBehavior.Restrict);
                }
                else
                {
                    owner.Ignore(value => value.ReviewNodes);
                    node.Ignore(value => value.OwnerScope);
                    node.Ignore(value => value.Reviewer);
                    node.Ignore(value => value.ReviewerId);
                    reference
                        .WithMany(value => value.Nodes)
                        .HasForeignKey(value => value.OwnerId)
                        .OnDelete(DeleteBehavior.Restrict);
                }
            }
        }
    }

    /// <summary>Exercises two composite FKs sharing ordinary scope payload.</summary>
    private sealed class CompositeRollbackContext : RollbackContext
    {
        /// <inheritdoc />
        protected override bool CompositeOwner => true;
    }

    /// <summary>Allows a distinct tracked victim at a hybrid tuple during one-to-one relationship repair.</summary>
    private sealed class UniqueRollbackContext : RollbackContext
    {
        /// <inheritdoc />
        protected override bool CompositeOwner => true;

        /// <inheritdoc />
        protected override bool UniqueOwner => true;
    }

    /// <summary>Preserves the actual temporary-key sidecar of a newly Added ordinary principal.</summary>
    private sealed class GeneratedRollbackContext : RollbackContext
    {
        /// <inheritdoc />
        protected override bool GeneratedOwner => true;
    }

    /// <summary>Separates ordinary principal identity, current collections and unique relationship storage.</summary>
    private sealed class RollbackOwner
    {
        /// <summary>Gets or sets the ordinary principal scope used only by composite mappings.</summary>
        public int Scope { get; set; } = 7;

        /// <summary>Gets or sets the fixed or generated ordinary principal key.</summary>
        public int Id { get; set; }

        /// <summary>Gets ordered CLR membership independently of EF's native reference set.</summary>
        public List<RollbackNode> Nodes { get; } = [];

        /// <summary>Gets members of the second relationship sharing the dependent's ordinary scope.</summary>
        public List<RollbackNode> ReviewNodes { get; } = [];

        /// <summary>Gets or sets the optional dependent in the unique relationship control.</summary>
        public RollbackNode? UniqueNode { get; set; }
    }

    /// <summary>Provides unchanged ordinary FK payload beside a marked hierarchy ordering candidate.</summary>
    private sealed class RollbackNode : INestedSetNode<int, Guid>
    {
        /// <summary>Gets or sets the independent hierarchy parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the ordinary domain ordering criterion marked before snapshot capture.</summary>
        public string Name { get; set; } = "Before";

        /// <summary>Gets or sets the shared, nullable ordinary FK scope, independent of hierarchy identity.</summary>
        public int? OwnerScope { get; set; } = 7;

        /// <summary>Gets or sets the nullable owner-key component.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Gets or sets the current ordinary owner reference.</summary>
        public RollbackOwner? Owner { get; set; }

        /// <summary>Gets or sets the second ordinary owner-key component sharing OwnerScope.</summary>
        public int? ReviewerId { get; set; }

        /// <summary>Gets or sets the independent second ordinary principal reference.</summary>
        public RollbackOwner? Reviewer { get; set; }

        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; } = Guid.NewGuid();

        /// <inheritdoc />
        public long Left { get; set; } = 1;

        /// <inheritdoc />
        public long Right { get; set; } = 2;

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }
}
