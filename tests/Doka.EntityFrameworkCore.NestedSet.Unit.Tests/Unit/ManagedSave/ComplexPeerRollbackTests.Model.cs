namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexPeerRollbackTests
{
    /// <summary>Uses real optional complex and nested JSON collection metadata without database commands.</summary>
    private class PeerContext : DbContext
    {
        /// <summary>Keeps raw CLR edits independent of EF's original values and modification flags.</summary>
        internal PeerContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <summary>Gets whether the peer shares its principal's physical table.</summary>
        protected virtual bool Sharing => true;

        /// <summary>Gets whether only the entity key and complex values are mapped on the peer.</summary>
        protected virtual bool ComplexOnly => false;

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
            modelBuilder.Ignore<KeyOnlyPeer>();
            var peer = modelBuilder.Entity<ComplexPeer>();
            peer
                .Property(value => value.Id)
                .ValueGeneratedNever();

            peer
                .ComplexProperty(value => value.Optional)
                .IsRequired(false);

            peer.ComplexCollection(
                value => value.Items,
                items =>
                {
                    items.ToJson("Items");
                    items.ComplexCollection(value => value.Tags);
                });

            if (ComplexOnly)
            {
                peer.Ignore(value => value.Marker);
            }

            if (Sharing)
            {
                peer.ToTable("ComplexPeerRows");
                var root = modelBuilder.Entity<SharingRoot>();
                root.Ignore(value => value.KeyPeer);
                root.ToTable("ComplexPeerRows");
                root
                    .Property(value => value.Id)
                    .ValueGeneratedNever();

                root
                    .HasOne(value => value.Peer)
                    .WithOne(value => value.Root)
                    .HasForeignKey<ComplexPeer>(value => value.Id)
                    .OnDelete(DeleteBehavior.Cascade);

                root
                    .Navigation(value => value.Peer)
                    .IsRequired();
            }
            else
            {
                peer.Ignore(value => value.Root);
                modelBuilder.Ignore<SharingRoot>();
            }
        }
    }

    /// <summary>Provides an ordinary non-sharing entity as a no-promotion control.</summary>
    private sealed class PlainPeerContext : PeerContext
    {
        /// <inheritdoc />
        protected override bool Sharing => false;
    }

    /// <summary>Removes every directly mapped non-key scalar without removing supported complex data.</summary>
    private sealed class ComplexOnlyPeerContext : PeerContext
    {
        /// <inheritdoc />
        protected override bool ComplexOnly => true;
    }

    /// <summary>Validates a required table-sharing peer containing only the common primary key.</summary>
    private sealed class KeyOnlyContext : DbContext
    {
        /// <summary>Retains explicit caller-controlled detection for the provider promotion control.</summary>
        internal KeyOnlyContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

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
            modelBuilder.Ignore<ComplexPeer>();
            var root = modelBuilder.Entity<SharingRoot>();
            root.Ignore(value => value.Peer);
            root.ToTable("KeyPeerRows");
            root
                .Property(value => value.Id)
                .ValueGeneratedNever();

            var peer = modelBuilder.Entity<KeyOnlyPeer>();
            peer.ToTable("KeyPeerRows");
            peer
                .Property(value => value.Id)
                .ValueGeneratedNever();

            root
                .HasOne(value => value.KeyPeer)
                .WithOne(value => value.Root)
                .HasForeignKey<KeyOnlyPeer>(value => value.Id)
                .OnDelete(DeleteBehavior.Cascade);

            root
                .Navigation(value => value.KeyPeer)
                .IsRequired();
        }
    }

    /// <summary>Provides the same physical row as its required peer and one independent pending scalar.</summary>
    private sealed class SharingRoot
    {
        /// <summary>Gets or sets the shared fixed primary key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the principal's ordinary application payload.</summary>
        public string Name { get; set; } = "Stored root";

        /// <summary>Gets or sets the complex-valued sharing peer in the ordinary model.</summary>
        public ComplexPeer? Peer { get; set; }

        /// <summary>Gets or sets the key-only sharing peer in the separate fallback model.</summary>
        public KeyOnlyPeer? KeyPeer { get; set; }
    }

    /// <summary>Separates optional complex identity, collection identity and raw current payload.</summary>
    private sealed class ComplexPeer
    {
        /// <summary>Gets or sets the shared row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional principal reference in the table-sharing model.</summary>
        public SharingRoot? Root { get; set; }

        /// <summary>Gets or sets an ordinary scalar absent from the complex-only model.</summary>
        public string Marker { get; set; } = "Stored marker";

        /// <summary>Gets or sets the optional complex instance independently of its stored original.</summary>
        public ComplexReference? Optional { get; set; } = new();

        /// <summary>Gets or sets the actual ordered complex collection mapped to JSON.</summary>
        public List<ComplexItem> Items { get; set; } = [];
    }

    /// <summary>Provides an optional complex reference with a required existence-determining scalar.</summary>
    private sealed class ComplexReference
    {
        /// <summary>Gets or sets the required current scalar when the optional instance exists.</summary>
        public string Text { get; set; } = "Stored reference";

        /// <summary>Gets or sets an independently nullable complex scalar.</summary>
        public string? Code { get; set; } = "Stored code";
    }

    /// <summary>Provides one mutable complex element and an independently stored nested collection.</summary>
    private sealed class ComplexItem
    {
        /// <summary>Gets or sets the collection element's ordinary scalar.</summary>
        public string Text { get; set; } = "";

        /// <summary>Gets or sets nested complex collection storage inside the outer JSON document.</summary>
        public List<ComplexTag> Tags { get; set; } = [];
    }

    /// <summary>Exposes raw nested complex payload without entity identity or entity state.</summary>
    private sealed class ComplexTag
    {
        /// <summary>Gets or sets the nested complex scalar.</summary>
        public string Text { get; set; } = "Stored tag";
    }

    /// <summary>Has no scalar that may legally be marked modified to restore the original Unchanged state.</summary>
    private sealed class KeyOnlyPeer
    {
        /// <summary>Gets or sets the shared fixed primary key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the required table-sharing principal reference.</summary>
        public SharingRoot? Root { get; set; }
    }
}
