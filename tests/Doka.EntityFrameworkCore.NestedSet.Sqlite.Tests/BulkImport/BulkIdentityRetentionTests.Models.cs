using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkIdentityRetentionTests
{
    /// <summary>Maps explicit shallow binary snapshots while keeping structural comparer semantics.</summary>
    private sealed class ShallowBinaryContext(DbContextOptions<ShallowBinaryContext> options)
        : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            var binary = builder.Entity<BinaryNode>();
            binary.ToTable("ShallowIdentityImportNodes");
            binary.HasKey(node => new
            {
                node.Scope,
                node.Id,
            });

            binary
                .Property(node => node.Id)
                .ValueGeneratedNever();

            foreach (var property in new[] { nameof(BinaryNode.Id), nameof(BinaryNode.Scope) })
            {
                binary
                    .Property<byte[]>(property)
                    .Metadata
                    .SetValueComparer(
                        new ValueComparer<byte[]>(
                            (left, right) => left != null && right != null && ((IEnumerable<byte>)left).SequenceEqual(right),
                            value => value.Length == 0 ? 0 : value[0],
                            value => value));
            }

            binary.HasNestedSet(set => set
                .HasNodeKey(node => node.Id)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Scope)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
            binary.OwnsOne(node => node.Payload);
            builder
                .Entity<IdentityMarker>()
                .ToTable("ShallowIdentityImportMarkers");
        }
    }

    /// <summary>Maps generated, binary and converted-class identities with relational owned dependents.</summary>
    private sealed class IdentityContext(DbContextOptions<IdentityContext> options) : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            builder
                .Entity<ScalarNode>()
                .ToTable("ImportIdentityScalarNodes");
            builder
                .Entity<ScalarNode>()
                .HasNestedSet(set => set
                    .HasTreeId(node => node.TreeId)
                    .HasParent(node => node.ParentId)
                    .HasBounds(node => node.Left, node => node.Right)
                    .HasDepth(node => node.Depth)
                    .HasPosition(node => node.Position));

            var binary = builder.Entity<BinaryNode>();
            binary.ToTable("ImportIdentityBinaryNodes");
            binary.HasKey(node => new
            {
                node.Scope,
                node.Id,
            });

            binary
                .Property(node => node.Id)
                .ValueGeneratedNever();
            binary.HasNestedSet(set => set
                .HasNodeKey(node => node.Id)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Scope)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
            binary.OwnsOne(node => node.Payload);

            var mutable = builder.Entity<MutableNode>();
            mutable.ToTable("ImportIdentityMutableNodes");

            foreach (var name in new[]
                     {
                         nameof(MutableNode.Id), nameof(MutableNode.Scope), nameof(MutableNode.ParentId),
                     })
            {
                var property = mutable.Property<MutableIdentity>(name);
                property
                    .HasMaxLength(40)
                    .HasConversion(value => value.Value, value => new MutableIdentity(value));
                property.Metadata.SetValueComparer(
                    new ValueComparer<MutableIdentity>(
                        (left, right) => left != null && right != null && left.Value == right.Value,
                        value => StringComparer.Ordinal.GetHashCode(value.Value),
                        value => new MutableIdentity(value.Value)));
            }

            mutable.HasKey(node => new
            {
                node.Scope,
                node.Id,
            });

            mutable
                .Property(node => node.Id)
                .ValueGeneratedNever();
            mutable.HasNestedSet(set => set
                .HasNodeKey(node => node.Id)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Scope)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
            mutable.OwnsOne(node => node.Payload);
            builder
                .Entity<IdentityMarker>()
                .ToTable("ImportIdentityMarkers");
        }
    }

    /// <summary>Provides an independently cloned mutable converted key representation.</summary>
    private sealed class MutableIdentity(string value)
    {
        /// <summary>Gets or sets the observable mutable identity representation.</summary>
        public string Value { get; set; } = value;
    }

    /// <summary>Provides a conventional generated scalar identity and unscoped hierarchy.</summary>
    private sealed class ScalarNode
    {
        /// <summary>Gets or sets the assigned or database-generated node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the tree root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Provides composite binary keys whose live buffers must not alias EF identity-map keys.</summary>
    private sealed class BinaryNode
    {
        /// <summary>Gets or sets the mutable binary node identity.</summary>
        public byte[] Id { get; set; } = [];

        /// <summary>Gets or sets the mutable binary scope identity.</summary>
        public byte[] Scope { get; set; } = [];

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional binary parent identity.</summary>
        public byte[]? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the tree root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the owned dependent whose native keys share principal values.</summary>
        public IdentityPayload Payload { get; set; } = new();
    }

    /// <summary>Provides independently mutable principal and composite scope identities.</summary>
    private sealed class MutableNode
    {
        /// <summary>Gets or sets the converted mutable node identity.</summary>
        public MutableIdentity Id { get; set; } = new(string.Empty);

        /// <summary>Gets or sets the converted mutable scope identity.</summary>
        public MutableIdentity Scope { get; set; } = new(string.Empty);

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional converted parent identity.</summary>
        public MutableIdentity? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the tree root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the owned dependent whose native keys share principal values.</summary>
        public IdentityPayload Payload { get; set; } = new();
    }

    /// <summary>Provides an owned composite-key dependent sharing its principal's identity values.</summary>
    private sealed class IdentityPayload
    {
        /// <summary>Gets or sets the payload preserved for a later insertion retry.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Provides unrelated application tracking that cleanup must preserve.</summary>
    private sealed class IdentityMarker
    {
        /// <summary>Gets or sets the independent application identity.</summary>
        public int Id { get; set; }
    }
}
