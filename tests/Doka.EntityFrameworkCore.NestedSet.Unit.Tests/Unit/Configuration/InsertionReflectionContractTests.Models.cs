namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionReflectionContractTests
{
    /// <summary>Uses the shared real-notification model with originals retained for the pause/resume control.</summary>
    private sealed class NotificationBindingContext : HierarchyChangeDetectionTests.ProbeContext
    {
        /// <inheritdoc />
        protected override ChangeTrackingStrategy TrackingStrategy =>
            ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues;
    }

    /// <summary>Supplies real scalar, reference and collection snapshot slots without I/O.</summary>
    private sealed class RelationshipContext : DbContext
    {
        /// <summary>Keeps deliberately raw navigation changes separate from native snapshot writes.</summary>
        internal RelationshipContext()
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
            var entity = modelBuilder.Entity<RelationshipEntity>();
            entity
                .Property(node => node.Id)
                .ValueGeneratedNever();

            entity
                .HasOne(node => node.Parent)
                .WithMany(node => node.Children)
                .HasForeignKey(node => node.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Collides in domain equality while native snapshot membership remains by reference.</summary>
    private sealed class RelationshipEntity
    {
        /// <summary>Gets or sets the explicitly assigned key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional ordinary self-reference key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the ordinary reference whose snapshot is independent of its current value.</summary>
        public RelationshipEntity? Parent { get; set; }

        /// <summary>Gets the current list, whose duplicate references are deduplicated in native snapshots.</summary>
        public List<RelationshipEntity> Children { get; } = [];

        /// <inheritdoc />
        public override bool Equals(
            object? other
        ) => other is RelationshipEntity entity && entity.Id == Id;

        /// <inheritdoc />
        public override int GetHashCode() => Id;
    }

    private sealed class SnapshotContext : DbContext
    {
        /// <summary>Creates a context whose compound key has native relationship snapshots.</summary>
        /// <param name="options">The in-memory model's provider options.</param>
        public SnapshotContext(
            DbContextOptions<SnapshotContext> options
        ) : base(options) { }

        /// <summary>Configures the two-property principal key used by the typed snapshot control.</summary>
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<SnapshotEntity>()
            .HasKey(entity => new
            {
                entity.First,
                entity.Second,
            });
    }

    private sealed class SnapshotEntity
    {
        /// <summary>Gets or sets the first compound principal-key component.</summary>
        public int First { get; set; }

        /// <summary>Gets or sets the second compound principal-key component.</summary>
        public int Second { get; set; }
    }
}
