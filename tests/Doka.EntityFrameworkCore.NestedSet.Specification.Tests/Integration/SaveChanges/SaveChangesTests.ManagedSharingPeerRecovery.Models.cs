namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Combines the hierarchy model with an independent shared application row.</summary>
    private sealed class ManagedSharingContext(DbContextOptions options, string shape)
        : NestedSetDbContext(options), ITestModelVariant
    {
        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => shape;

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureNavigationModel(modelBuilder);
            var row = modelBuilder.Entity<ManagedSharingRow>();
            row.ToTable("ManagedSharingRows");
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.Text).HasMaxLength(128);
            row.Property(value => value.Version).HasColumnName("Version").IsConcurrencyToken();

            if (shape == "owned")
            {
                row.OwnsOne(value => value.Payload, payload =>
                {
                    payload.WithOwner(value => value.Row).HasForeignKey(value => value.Id);
                    payload.HasKey(value => value.Id);
                    payload.Property(value => value.Id).HasColumnName("Id").ValueGeneratedNever();
                    payload.Property(value => value.Value).HasColumnName("PayloadValue").HasMaxLength(128);
                    payload.Property(value => value.Version).HasColumnName("Version").IsConcurrencyToken();
                });
                row.Navigation(value => value.Payload).IsRequired();
            }
            else if (shape == "split")
            {
                var payload = modelBuilder.Entity<ManagedSharingPayload>();
                payload.ToTable("ManagedSharingRows");
                payload.Property(value => value.Id).ValueGeneratedNever();
                payload.Property(value => value.Value).HasColumnName("PayloadValue").HasMaxLength(128);
                // WHY: Every entity sharing this row maps the same concurrency-token column.
                payload.Property(value => value.Version).HasColumnName("Version").IsConcurrencyToken();
                row.HasOne(value => value.Payload).WithOne(value => value.Row)
                    .HasForeignKey<ManagedSharingPayload>(value => value.Id).OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                throw new InvalidOperationException("The managed sharing model shape is not recognized.");
            }
        }
    }

    /// <summary>Represents an ordinary application row written explicitly by a hierarchy insertion callback.</summary>
    private sealed class ManagedSharingRow
    {
        /// <summary>Gets or sets the assigned application identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the callback's ordinary root payload.</summary>
        public string Text { get; set; } = "original row";

        /// <summary>Gets or sets the application-managed concurrency token.</summary>
        public int Version { get; set; } = 7;

        /// <summary>Gets or sets the owned or ordinary entity sharing this row.</summary>
        public ManagedSharingPayload? Payload { get; set; }
    }

    /// <summary>Provides the unchanged sharing peer whose provider promotion must be rolled back.</summary>
    private sealed class ManagedSharingPayload
    {
        /// <summary>Gets or sets the shared identifying foreign key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the shared concurrency token.</summary>
        public int Version { get; set; } = 7;

        /// <summary>Gets or sets ordinary dependent payload.</summary>
        public string Value { get; set; } = "original payload";

        /// <summary>Gets or sets the application principal occupying the same physical row.</summary>
        public ManagedSharingRow? Row { get; set; }
    }
}
