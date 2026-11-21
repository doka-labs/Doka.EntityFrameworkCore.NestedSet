namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TrackedIdentityCaptureTests
{
    /// <summary>Provides a converted identity whose provider work is observable without database commands.</summary>
    private readonly record struct CountedIdentity(
        int Value
    );

    /// <summary>Maps two converted identity roles to ordinary scalar integer columns.</summary>
    private sealed class CountedContext : DbContext
    {
        /// <summary>Creates a model-only context whose database connection remains closed.</summary>
        internal CountedContext() : base(
            new DbContextOptionsBuilder<CountedContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<CountedNode>();
            node.ToTable("TrackedIdentityCountedNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Scope)
                .HasConversion(value => CountProviderConversion(value), value => new CountedIdentity(value));

            node
                .Property(value => value.TreeId)
                .HasConversion(value => CountProviderConversion(value), value => new CountedIdentity(value));

            node.HasNestedSet(builder => builder
                .HasScope(value => value.Scope)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId));
        }
    }

    /// <summary>Maps binary identities with deliberately shallow domain snapshots.</summary>
    private sealed class BinaryContext : DbContext
    {
        /// <summary>Creates a model-only binary hierarchy context.</summary>
        internal BinaryContext() : base(
            new DbContextOptionsBuilder<BinaryContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<BinaryNode>();
            node.ToTable("TrackedIdentityBinaryNodes");
            var comparer = new ValueComparer<byte[]>(
                (first, second) => first == second
                    || (first != null
                        && second != null
                        && first
                            .AsEnumerable()
                            .SequenceEqual(second)),
                value => value.Length,
                value => value);

            // WHY: Domain comparers may intentionally avoid copying arrays. The guard must independently own
            // every mutable identity snapshot rather than trusting these model-level snapshot semantics.
            node
                .Property(value => value.Id)
                .ValueGeneratedNever()
                .HasMaxLength(8)
                .Metadata
                .SetValueComparer(comparer);

            node
                .Property(value => value.Scope)
                .HasMaxLength(8)
                .Metadata
                .SetValueComparer(comparer);

            node
                .Property(value => value.TreeId)
                .HasMaxLength(8)
                .Metadata
                .SetValueComparer(comparer);

            node
                .Property(value => value.ParentId)
                .HasMaxLength(8);

            node.HasNestedSet(builder => builder
                .HasScope(value => value.Scope)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId));
        }
    }

    /// <summary>Separates counted provider roles from the native integer node key.</summary>
    private sealed class CountedNode : IScopedNestedSetNode<int, CountedIdentity, CountedIdentity>
    {
        /// <summary>Gets or sets the optional direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <inheritdoc />
        public int Id { get; set; }

        /// <inheritdoc />
        public CountedIdentity Scope { get; set; }

        /// <inheritdoc />
        public CountedIdentity TreeId { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Exposes independently mutable binary roles for late verification.</summary>
    private sealed class BinaryNode : IScopedNestedSetNode<byte[], byte[], byte[]>
    {
        /// <summary>Gets or sets the optional direct parent key.</summary>
        public byte[]? ParentId { get; set; }

        /// <inheritdoc />
        public byte[] Id { get; set; } = [];

        /// <inheritdoc />
        public byte[] Scope { get; set; } = [];

        /// <inheritdoc />
        public byte[] TreeId { get; set; } = [];

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
