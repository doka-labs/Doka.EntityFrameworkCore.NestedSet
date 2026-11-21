namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class InsertionRefreshAllocationTests
{
    /// <summary>Finalizes the production EF identity shapes without database I/O.</summary>
    private sealed class AllocationContext : DbContext
    {
        /// <summary>Supplies relational metadata without opening a connection.</summary>
        protected override void OnConfiguring(
            DbContextOptionsBuilder options
        ) => options.UseSqlite("Data Source=:memory:");

        /// <summary>Configures the scalar, sidecar, relationship, and property-bag comparison controls.</summary>
        protected override void OnModelCreating(
            ModelBuilder model
        )
        {
            model
                .Entity<IntNode>()
                .HasKey(node => node.Id);

            model
                .Entity<IntNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<GuidNode>()
                .HasKey(node => node.Id);

            model
                .Entity<GuidNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<CompoundNode>()
                .HasKey(node => new
                {
                    node.Scope,
                    node.Id,
                });

            model
                .Entity<CompoundNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<GeneratedNode>()
                .HasKey(node => node.Id);

            model
                .Entity<GeneratedNode>()
                .Property(node => node.Id)
                .ValueGeneratedOnAdd();

            model
                .Entity<ShadowNode>()
                .Property<int>("Id")
                .ValueGeneratedNever();

            model
                .Entity<ShadowNode>()
                .HasKey("Id");

            model
                .Entity<StringNode>()
                .HasKey(node => node.Id);

            var text = model
                .Entity<StringNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            text.Metadata.SetValueComparer(
                new ValueComparer<string>(
                    (left, right) => NonNullTextEquals(left, right),
                    value => value.GetHashCode(),
                    value => value));

            model
                .Entity<FieldNode>()
                .HasKey(node => node.Id);

            model
                .Entity<FieldNode>()
                .Property(node => node.Id)
                .HasField("_id")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .ValueGeneratedNever();

            model.SharedTypeEntity<Dictionary<string, object>>(
                "IndexerNode",
                bag =>
                {
                    bag
                        .IndexerProperty<int>("Id")
                        .HasSentinel(37)
                        .ValueGeneratedNever();

                    bag.HasKey("Id");
                });

            model
                .Entity<ForeignNode>()
                .HasKey(node => node.Id);

            model
                .Entity<ForeignNode>()
                .Property(node => node.Id)
                .ValueGeneratedNever();

            model
                .Entity<ForeignNode>()
                .HasOne<ForeignNode>()
                .WithMany()
                .HasForeignKey(node => node.ParentId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        }
    }

    private sealed class IntNode
    {
        /// <summary>Gets or sets the assigned scalar identity.</summary>
        public int Id { get; set; }
    }

    private sealed class GuidNode
    {
        /// <summary>Gets or sets the assigned Guid identity.</summary>
        public Guid Id { get; set; }
    }

    private sealed class CompoundNode
    {
        /// <summary>Gets or sets the first composite key component.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the second composite key component.</summary>
        public int Id { get; set; }
    }

    private sealed class GeneratedNode
    {
        /// <summary>Gets or sets the provider-generated scalar identity.</summary>
        public int Id { get; set; }
    }

    private sealed class ShadowNode;

    private sealed class StringNode
    {
        /// <summary>Gets or sets the immutable reference identity.</summary>
        public string? Id { get; set; } = string.Empty;
    }

    private sealed class FieldNode
    {
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public FieldNode(
            int id
        )
        {
            _id = id;
        }

        private readonly int _id;

        /// <summary>Gets the identity read through its configured backing field.</summary>
        // WHY: EF explicitly binds _id; an auto-property would invalidate the field-access fixture.
        // ReSharper disable once ConvertToAutoProperty
        public int Id => _id;
    }

    private sealed class ForeignNode
    {
        /// <summary>Gets or sets the assigned identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the required FK whose tracker value can be conceptually null.</summary>
        public int ParentId { get; set; }
    }
}
