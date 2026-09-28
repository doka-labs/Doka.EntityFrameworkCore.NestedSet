namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingRefreshTests
{
    /// <summary>Maps native binary scope equality on the fixture-owned provider and connection.</summary>
    private sealed class BinaryScopeContext : NestedSetDbContext
    {
        /// <summary>Uses independently named tables with the existing provider configuration.</summary>
        internal BinaryScopeContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryScopeNode>();
            node.ToTable("BinaryScopeNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.Scope)
                .HasMaxLength(8);
            node
                .Property(value => value.Name)
                .HasMaxLength(80);
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId)
                .OrderBy(value => value.Name));
        }
    }

    /// <summary>Separates binary scope identity from independently tracked integer node keys.</summary>
    private sealed class BinaryScopeNode : IScopedNestedSetNode<int, Guid, byte[]>
    {
        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Id { get; set; }

        /// <summary>Gets or sets the binary forest identity.</summary>
        public byte[] Scope { get; set; } = [];

        /// <summary>Gets or sets the optional direct parent.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the configured sibling sort value.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets domain payload excluded from structural refresh queries.</summary>
        public string Payload { get; set; } = "original payload";

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
