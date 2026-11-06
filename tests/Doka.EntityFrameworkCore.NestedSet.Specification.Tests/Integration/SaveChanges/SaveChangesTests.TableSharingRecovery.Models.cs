namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Coordinates equivalent ordered-node models with owned, ordinary split or unsplit payload.</summary>
    private sealed class TableSharingRecoveryContext(
        DbContextOptions options,
        string shape
    )
        : NestedSetDbContext(options), ITestModelVariant
    {
        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => shape;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<TableSharingRecoveryNode>();

            // WHY: Isolated databases reuse the existing table-aware payload and structural fault probes.
            node.ToTable("NavigationSaveNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Name)
                .HasMaxLength(128);

            node
                .Property(value => value.Payload)
                .HasMaxLength(128);

            node
                .Property(value => value.Version)
                .HasColumnName("Version")
                .IsConcurrencyToken();

            // WHY: Structure-only ordering writes intentionally preserve this application-managed token.
            node.HasNestedSet(value => value
                .HasTreeId(item => item.TreeId)
                .HasParent(item => item.ParentId)
                .OrderBy(item => item.Name)
                .PreserveApplicationConcurrencyTokens());

            switch (shape)
            {
                case "owned":
                    node.Ignore(value => value.Peer);
                    node.OwnsOne(
                        value => value.Owned,
                        payload =>
                        {
                            payload
                                .WithOwner()
                                .HasForeignKey(value => value.Id);

                            payload.HasKey(value => value.Id);
                            payload
                                .Property(value => value.Id)
                                .HasColumnName("Id")
                                .ValueGeneratedNever();

                            payload
                                .Property(value => value.Value)
                                .HasColumnName("OwnedValue")
                                .HasMaxLength(128);

                            // WHY: Every entity sharing a row must map the same concurrency token to the same column.
                            payload
                                .Property(value => value.Version)
                                .HasColumnName("Version")
                                .IsConcurrencyToken();
                        });

                    node
                        .Navigation(value => value.Owned)
                        .IsRequired();

                    break;
                case "split":
                    node.Ignore(value => value.Owned);
                    var peer = modelBuilder.Entity<TableSharingRecoveryPeer>();
                    peer.ToTable("NavigationSaveNodes");
                    peer
                        .Property(value => value.Id)
                        .ValueGeneratedNever();

                    peer
                        .Property(value => value.Value)
                        .HasColumnName("PeerValue")
                        .HasMaxLength(128);

                    peer
                        .Property(value => value.Version)
                        .HasColumnName("Version")
                        .IsConcurrencyToken();

                    node
                        .HasOne(value => value.Peer)
                        .WithOne(value => value.Node)
                        .HasForeignKey<TableSharingRecoveryPeer>(value => value.Id)
                        .OnDelete(DeleteBehavior.Cascade);

                    break;
                case "none":
                    node.Ignore(value => value.Owned);
                    node.Ignore(value => value.Peer);
                    break;
                default:
                    throw new InvalidOperationException("The table-sharing test model shape is not recognized.");
            }
        }
    }

    /// <summary>Provides ordered structure and an application-managed concurrency token for every shape.</summary>
    private sealed class TableSharingRecoveryNode : INestedSetNode<int, Guid>
    {
        /// <summary>Gets or sets the configured optional parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the order criterion explicitly modified by each coordinated save.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets ordinary payload for the unsplit control.</summary>
        public string Payload { get; set; } = "original payload";

        /// <summary>Gets or sets the token shared with every entity mapped to this physical row.</summary>
        public int Version { get; set; } = 7;

        /// <summary>Gets or sets the owned payload used only by the ownership model.</summary>
        public TableSharingRecoveryOwned? Owned { get; set; }

        /// <summary>Gets or sets the ordinary payload entity used only by the table-split model.</summary>
        public TableSharingRecoveryPeer? Peer { get; set; }

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

    /// <summary>Shares the node's physical row through an ordinary OwnsOne mapping.</summary>
    private sealed class TableSharingRecoveryOwned
    {
        /// <summary>Gets or sets the shared identifying foreign key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the shared concurrency token.</summary>
        public int Version { get; } = 7;

        /// <summary>Gets or sets ordinary payload that must remain unchanged unless explicitly marked.</summary>
        public string Value { get; set; } = "original owned";
    }

    /// <summary>Shares the node's row through an identifying one-to-one relationship without ownership.</summary>
    private sealed class TableSharingRecoveryPeer
    {
        /// <summary>Gets or sets the shared identifying foreign key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the shared concurrency token required by EF's table-sharing contract.</summary>
        public int Version { get; set; } = 7;

        /// <summary>Gets or sets the optional presence column populated for all stored split payloads.</summary>
        public string? Value { get; set; }

        /// <summary>Gets or sets the principal occupying the same physical row.</summary>
        public TableSharingRecoveryNode? Node { get; set; }
    }
}
