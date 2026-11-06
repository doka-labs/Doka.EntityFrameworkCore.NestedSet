namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Adds one unmarked secondary order criterion beside the modified candidate trigger.</summary>
    private sealed class SecondaryOrderingPlanContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ProbeNode>()
                .HasNestedSet(builder => builder
                    .OrderBy(value => value.Name)
                    .ThenBy(value => value.Payload));
        }
    }

    /// <summary>Uses a shadow position role while retaining an ordinary CLR property with different meaning.</summary>
    private sealed class ShadowPositionPlanContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            var node = modelBuilder.Entity<ProbeNode>();
            node.Ignore(value => value.Position);
            node.Property<long>("ShadowPosition");
            node.HasNestedSet(builder => builder.HasPosition("ShadowPosition"));
        }
    }

    /// <summary>Allocates the real store-generated identity sidecar used by provider readback.</summary>
    private sealed class GeneratedNodePlanContext : ProbeContext
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ProbeNode>()
                .Property(value => value.Id)
                .ValueGeneratedOnAdd();
        }
    }

    /// <summary>Maps independently cloned mutable ordering values through a scalar provider conversion.</summary>
    private sealed class MutableOrderingPlanContext : DbContext
    {
        /// <summary>Keeps every test mutation independent of EF's automatic detection.</summary>
        internal MutableOrderingPlanContext()
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
            var node = modelBuilder.Entity<MutablePlanNode>();
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Rank)
                .HasConversion(
                    value => Convert.ToBase64String(value),
                    value => Convert.FromBase64String(value))
                .Metadata
                .SetValueComparer(
                    new ValueComparer<byte[]>(
                        (left, right) => Enumerable.SequenceEqual(left!, right!),
                        value => value.Length,
                        value => value.ToArray()));

            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .OrderBy(value => value.Rank));
        }
    }

    /// <summary>Provides a mutable domain ordering value beside ordinary nested-set scalar roles.</summary>
    private sealed class MutablePlanNode : INestedSetNode<int, Guid>
    {
        /// <summary>Gets or sets the optional direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the domain criterion with a deep snapshot comparer.</summary>
        public byte[] Rank { get; set; } = [];

        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

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
