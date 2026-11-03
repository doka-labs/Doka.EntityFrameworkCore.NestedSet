namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TableSharingRollbackTests
{
    private const string VersionProperty = "Version";

    /// <summary>Validates actual owned or ordinary table-sharing metadata without opening SQLite.</summary>
    private class SharingContext : DbContext
    {
        /// <summary>Keeps raw current payload independent of original values and modification flags.</summary>
        internal SharingContext() => ChangeTracker.AutoDetectChangesEnabled = false;

        /// <summary>Gets whether the sharing detail is an owned dependent.</summary>
        protected virtual bool OwnedDetail => true;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlite("Data Source=:memory:");

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var root = modelBuilder.Entity<SharingRoot>();
            root.ToTable("SharingRows");
            root
                .Property(value => value.Id)
                .ValueGeneratedOnAdd();
            root
                .Property<byte[]>(VersionProperty)
                .HasColumnName(VersionProperty)
                .IsRowVersion()
                .IsRequired();

            if (OwnedDetail)
            {
                root.OwnsOne(value => value.Detail,
                    detail =>
                    {
                        detail.ToTable("SharingRows");
                        detail
                            .WithOwner(value => value.Root)
                            .HasForeignKey(value => value.Id);
                        detail.HasKey(value => value.Id);
                        detail
                            .Property(value => value.Id)
                            .HasColumnName(nameof(SharingRoot.Id))
                            .ValueGeneratedNever();
                        detail
                            .Property<byte[]>(VersionProperty)
                            .HasColumnName(VersionProperty)
                            .IsRowVersion()
                            .IsRequired();
                    });
            }
            else
            {
                var detail = modelBuilder.Entity<SharingDetail>();
                detail.ToTable("SharingRows");
                detail
                    .Property(value => value.Id)
                    .HasColumnName(nameof(SharingRoot.Id))
                    .ValueGeneratedNever();
                detail
                    .HasOne(value => value.Root)
                    .WithOne(value => value.Detail)
                    .HasForeignKey<SharingDetail>(value => value.Id)
                    .OnDelete(DeleteBehavior.Cascade);
                detail
                    .Property<byte[]>(VersionProperty)
                    .HasColumnName(VersionProperty)
                    .IsRowVersion()
                    .IsRequired();
            }

            // WHY: A table-sharing concurrency column must exist on every alias, and the detail is required
            // so the model does not depend on optional-dependent existence heuristics or invalid token mappings.
            root
                .Navigation(value => value.Detail)
                .IsRequired();
        }
    }

    /// <summary>Uses ordinary required one-to-one table splitting instead of EF ownership.</summary>
    private sealed class SplitSharingContext : SharingContext
    {
        /// <inheritdoc />
        protected override bool OwnedDetail => false;
    }

    /// <summary>Owns the shared row identity and an independently modified ordinary application column.</summary>
    private sealed class SharingRoot
    {
        /// <summary>Gets or sets the shared fixed or generated row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the root's ordinary payload column.</summary>
        public string Name { get; set; } = "Stored root";

        /// <summary>Gets or sets the required row-sharing detail.</summary>
        public SharingDetail? Detail { get; set; }
    }

    /// <summary>Separates unmarked current CLR payload from stored originals on the same physical row.</summary>
    private sealed class SharingDetail
    {
        /// <summary>Gets or sets the shared PK/FK row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the required principal reference.</summary>
        public SharingRoot? Root { get; set; }

        /// <summary>Gets or sets a column that may be modified independently of the other detail columns.</summary>
        public string Label { get; set; } = "Stored label";

        /// <summary>Gets or sets a raw, unmarked payload value whose original remains independently captured.</summary>
        public int Count { get; set; } = 1;
    }
}
