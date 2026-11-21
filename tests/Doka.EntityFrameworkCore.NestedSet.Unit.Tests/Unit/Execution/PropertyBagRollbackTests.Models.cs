namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class PropertyBagRollbackTests
{
    /// <summary>Uses a relational property-bag model without opening a database connection.</summary>
    private sealed class BagContext : DbContext
    {
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
            modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(
                Mapping,
                entity =>
                {
                    entity
                        .IndexerProperty<int>("Id")
                        .ValueGeneratedNever();

                    entity.HasKey("Id");
                    entity.IndexerProperty<string>("Payload");
                    entity
                        .IndexerProperty<bool>(Flag)
                        .ValueGeneratedOnAddOrUpdate();
                });

            modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(
                BulkMapping,
                entity =>
                {
                    entity
                        .IndexerProperty<int>("Id")
                        .HasSentinel(0)
                        .ValueGeneratedNever();

                    entity.IndexerProperty<string>("Payload");
                    entity
                        .IndexerProperty<Guid>(NestedSetPropertyRoles.TreeId)
                        .HasSentinel(Guid.Empty);

                    entity.IndexerProperty<int?>(NestedSetPropertyRoles.Parent);
                    entity
                        .IndexerProperty<long>(NestedSetPropertyRoles.Left)
                        .HasSentinel(0L);

                    entity
                        .IndexerProperty<long>(NestedSetPropertyRoles.Right)
                        .HasSentinel(0L);

                    entity
                        .IndexerProperty<int>(NestedSetPropertyRoles.Depth)
                        .HasSentinel(0);

                    entity
                        .IndexerProperty<long>(NestedSetPropertyRoles.Position)
                        .HasSentinel(0L);

                    entity.HasKey("Id");
                    entity.HasNestedSet(mapping => mapping
                        .HasNodeKey("Id")
                        .HasTreeId(NestedSetPropertyRoles.TreeId)
                        .HasParent(NestedSetPropertyRoles.Parent)
                        .HasBounds(NestedSetPropertyRoles.Left, NestedSetPropertyRoles.Right)
                        .HasDepth(NestedSetPropertyRoles.Depth)
                        .HasPosition(NestedSetPropertyRoles.Position));
                });

            modelBuilder
                .Entity<CustomIndexerEntity>()
                .Property(entity => entity.Id)
                .ValueGeneratedNever();

            modelBuilder
                .Entity<CustomIndexerEntity>()
                .IndexerProperty<bool>(Flag)
                .ValueGeneratedOnAddOrUpdate();

            modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(
                GeneratedMapping,
                entity =>
                {
                    entity
                        .IndexerProperty<int>("Id")
                        .ValueGeneratedOnAdd();

                    entity.HasKey("Id");
                });
        }
    }

    /// <summary>Exposes a mapped indexer without promising dictionary membership operations.</summary>
    private sealed class CustomIndexerEntity
    {
        private object? _value;

        /// <summary>Gets or sets the assigned identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the sole custom generated value through its configured indexer.</summary>
        public object? this[
            string name
        ]
        {
            get => name == Flag
                ? _value
                : throw new ArgumentOutOfRangeException(nameof(name));
            set => _value = name == Flag
                ? value
                : throw new ArgumentOutOfRangeException(nameof(name));
        }
    }
}
