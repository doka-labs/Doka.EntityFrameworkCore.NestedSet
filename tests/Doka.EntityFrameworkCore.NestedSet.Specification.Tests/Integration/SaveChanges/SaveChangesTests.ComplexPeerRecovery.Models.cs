using System.ComponentModel.DataAnnotations.Schema;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Configures portable reference-type complex values and a JSON complex collection.</summary>
    private static void ConfigureComplexPeerPayload(
        IMutableEntityType payload,
        bool optionalPeer
    )
    {
        // WHY: OwnedNavigationBuilder has no complex-collection fluent API. Public mutable metadata configures
        // the same supported model without constructing EF's infrastructure-only entity builders.
        var marker = payload.FindProperty(nameof(ComplexPeerPayload.Marker))
            ?? payload.AddProperty(nameof(ComplexPeerPayload.Marker));

        marker.SetColumnName("Marker");
        marker.SetMaxLength(128);
        var address = payload.FindComplexProperty(nameof(ComplexPeerPayload.Address))
            ?? payload.AddComplexProperty(nameof(ComplexPeerPayload.Address), collection: false);

        address.IsNullable = true;
        var text = address.ComplexType.FindProperty(nameof(ComplexPeerAddress.Text))
            ?? address.ComplexType.AddProperty(nameof(ComplexPeerAddress.Text));

        // WHY: A required leaf distinguishes an optional complex reference from an all-null instance.
        text.IsNullable = false;
        text.SetColumnName("AddressText");
        text.SetMaxLength(128);
        var extra = address.ComplexType.FindProperty(nameof(ComplexPeerAddress.Extra))
            ?? address.ComplexType.AddProperty(nameof(ComplexPeerAddress.Extra));

        extra.IsNullable = true;
        extra.SetColumnName("AddressExtra");
        extra.SetMaxLength(128);
        var items = payload.FindComplexProperty(nameof(ComplexPeerPayload.Items))
            ?? payload.AddComplexProperty(nameof(ComplexPeerPayload.Items), collection: true);

        // WHY: The JSON column must also permit the absent optional split peer during principal insertion.
        // Present fixture peers always have a non-null collection, verified after seeding.
        items.IsNullable = optionalPeer;
        items.ComplexType.SetContainerColumnName("Items");
        var itemText = items.ComplexType.FindProperty(nameof(ComplexPeerItem.Text))
            ?? items.ComplexType.AddProperty(nameof(ComplexPeerItem.Text));

        itemText.IsNullable = false;
    }

    /// <summary>Maps the same complex payload through ownership, table splitting or a separate ordinary row.</summary>
    private sealed class ComplexPeerRecoveryContext(
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
            var node = modelBuilder.Entity<ComplexPeerRecoveryNode>();
            node.ToTable("NavigationSaveNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Name)
                .HasMaxLength(128);

            node.HasNestedSet(value => value
                .HasTreeId(item => item.TreeId)
                .HasParent(item => item.ParentId)
                .OrderBy(item => item.Name));

            if (shape == "owned")
            {
                var payload = node.OwnsOne(value => value.Details);
                payload
                    .WithOwner(value => value.Node)
                    .HasForeignKey(value => value.Id);

                payload.HasKey(value => value.Id);
                payload
                    .Property(value => value.Id)
                    .HasColumnName("Id")
                    .ValueGeneratedNever();

                ConfigureComplexPeerPayload(payload.OwnedEntityType, optionalPeer: false);
                node
                    .Navigation(value => value.Details)
                    .IsRequired();
            }
            else if (shape is "split" or "separate")
            {
                var payload = modelBuilder.Entity<ComplexPeerPayload>();
                payload.ToTable(
                    shape == "split"
                        ? "NavigationSaveNodes"
                        : "SeparateComplexPayloads");

                payload
                    .Property(value => value.Id)
                    .ValueGeneratedNever();

                ConfigureComplexPeerPayload(payload.Metadata, optionalPeer: shape == "split");
                node
                    .HasOne(value => value.Details)
                    .WithOne(value => value.Node)
                    .HasForeignKey<ComplexPeerPayload>(value => value.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            }
            else
            {
                throw new InvalidOperationException("The complex sharing model shape is not recognized.");
            }
        }
    }

    /// <summary>Provides an ordered hierarchy whose unchanged peer contains independent complex payload.</summary>
    private sealed class ComplexPeerRecoveryNode : INestedSetNode<int, Guid>
    {
        /// <summary>Gets or sets the configured optional parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the explicitly marked ordering criterion.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the ordinary peer configured by the current model shape.</summary>
        public ComplexPeerPayload? Details { get; set; }

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

    /// <summary>Contains unmarked complex CLR values whose object identity belongs to the caller.</summary>
    private sealed class ComplexPeerPayload
    {
        /// <summary>Gets or sets the identifying foreign key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional-dependent presence discriminator.</summary>
        public string? Marker { get; set; } = "original";

        /// <summary>Gets or sets an optional complex reference with one required leaf.</summary>
        public ComplexPeerAddress? Address { get; set; } = new();

        /// <summary>Gets or sets the reference-type JSON complex collection.</summary>
        public List<ComplexPeerItem> Items { get; set; } = [];

        /// <summary>Gets or sets the owning or identifying principal.</summary>
        public ComplexPeerRecoveryNode? Node { get; set; }
    }

    /// <summary>Provides a nullable complex reference independently of its containing entity.</summary>
    [ComplexType]
    private sealed class ComplexPeerAddress
    {
        /// <summary>Gets or sets the required leaf used to distinguish a present complex reference.</summary>
        public string Text { get; set; } = "original address";

        /// <summary>Gets or sets an optional scalar leaf.</summary>
        public string? Extra { get; set; }
    }

    /// <summary>Provides a reference complex collection element whose identity must survive recovery.</summary>
    [ComplexType]
    private sealed class ComplexPeerItem
    {
        /// <summary>Gets or sets the element's ordinary text payload.</summary>
        public string Text { get; set; } = string.Empty;
    }
}
