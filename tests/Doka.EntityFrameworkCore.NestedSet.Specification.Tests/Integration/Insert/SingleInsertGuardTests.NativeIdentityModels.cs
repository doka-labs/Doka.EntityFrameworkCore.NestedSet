namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>Maps converted mutable identities, owned payload, and a generated-key positive control.</summary>
    private sealed class SingleIdentityContext : DbContext
    {
        /// <summary>Uses the same qualified provider options as other model-compatibility tests.</summary>
        internal SingleIdentityContext(
            DbContextOptions<SingleIdentityContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<SingleIdentityNode>();
            node.ToTable("SingleIdentityNodes");
            node
                .Property(entity => entity.Id)
                .HasConversion(key => key.Value, value => new SingleIdentityKey(value))
                .ValueGeneratedNever()
                .UsePropertyAccessMode(PropertyAccessMode.Property)
                .Metadata
                .SetValueComparer(
                    new ValueComparer<SingleIdentityKey>(
                        (left, right) => left != null && right != null && left.Value == right.Value,
                        value => value.Value,
                        value => SnapshotSingleKey(value)));
            node.Ignore(entity => entity.ReadKey);
            node
                .Property(entity => entity.ParentId)
                .HasConversion(
                    key => key == null ? (int?)null : key.Value,
                    value => value.HasValue ? new SingleIdentityKey(value.Value) : null);
            node
                .Property(entity => entity.GeneratedValue)
                .HasDefaultValue(42);
            node.OwnsOne(
                entity => entity.Details,
                details =>
                {
                    details
                        .WithOwner()
                        .HasForeignKey(value => value.OwnerId);
                    details.HasKey(value => value.OwnerId);
                    details
                        .Property(value => value.OwnerId)
                        .HasConversion(key => key.Value, value => new SingleIdentityKey(value))
                        .ValueGeneratedNever()
                        .Metadata
                        .SetValueComparer(
                            new ValueComparer<SingleIdentityKey>(
                                (left, right) => left != null && right != null && left.Value == right.Value,
                                value => value.Value,
                                value => SnapshotSingleKey(value)));
                    details.Property(value => value.Label);
                    details
                        .Property(value => value.GeneratedValue)
                        .HasDefaultValue(43);
                    details
                        .HasOne(value => value.Foreign)
                        .WithMany()
                        .HasForeignKey("OwnedForeignId");
                });
            node
                .HasOne(entity => entity.Foreign)
                .WithMany()
                .HasForeignKey("ForeignId");
            node.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(entity => entity.Id)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position));

            var generated = modelBuilder.Entity<BulkStageBinaryScope>();
            generated.ToTable("SingleGeneratedIdentityNodes");
            generated
                .Property(entity => entity.Scope)
                .HasMaxLength(64);
            generated.HasNestedSet(nestedSet => nestedSet
                .HasTreeId(entity => entity.TreeId)
                .HasScope(entity => entity.Scope)
                .HasParent(entity => entity.ParentId));

            modelBuilder
                .Entity<UnrelatedRow>()
                .ToTable("SingleIdentityMarkers")
                .Property(entity => entity.Id)
                .ValueGeneratedNever();
            modelBuilder
                .Entity<SingleIdentityForeign>()
                .ToTable("SingleIdentityForeign")
                .Property(entity => entity.Id)
                .ValueGeneratedNever();
        }
    }

    /// <summary>Runs a one-shot application snapshot callback before creating an independent key.</summary>
    private static SingleIdentityKey SnapshotSingleKey(
        SingleIdentityKey key
    )
    {
        key.OnSnapshot?.Invoke();

        return new SingleIdentityKey(key.Value);
    }

    /// <summary>Exposes a mutable provider identity with an independent application comparer snapshot.</summary>
    private sealed class SingleIdentityKey
    {
        /// <summary>Creates a key using the exact provider integer.</summary>
        internal SingleIdentityKey(
            int value
        )
        {
            Value = value;
        }

        /// <summary>Gets or sets the mutable provider representation.</summary>
        internal int Value { get; set; }

        /// <summary>Gets or sets the one-shot application snapshot fault used by initialization regressions.</summary>
        internal Action? OnSnapshot { get; set; }
    }

    /// <summary>Contains the owned payload identity whose partial tracking must also be released.</summary>
    private sealed class SingleIdentityDetails
    {
        /// <summary>Gets or sets the public ownership key propagated by EF despite never being generated.</summary>
        public SingleIdentityKey OwnerId { get; set; } = new(0);

        /// <summary>Gets or sets the application payload.</summary>
        internal string Label { get; set; } = "";

        /// <summary>Gets or sets a provider-generated payload leaf restored after rollback.</summary>
        internal int GeneratedValue { get; set; }

        /// <summary>Gets or sets a non-owned relationship reached through the owned payload.</summary>
        internal SingleIdentityForeign? Foreign { get; set; }
    }

    /// <summary>Represents detached input with a converted mutable assigned identity.</summary>
    private sealed class SingleIdentityNode
    {
        private SingleIdentityKey _id = new(0);

        /// <summary>Gets or sets the exact converted primary key.</summary>
        internal SingleIdentityKey Id
        {
            get
            {
                ReadKey?.Invoke();

                return _id;
            }
            set => _id = value;
        }

        /// <summary>Gets or sets the one-shot structural getter fault used by initialization regressions.</summary>
        internal Action? ReadKey { get; set; }

        /// <summary>Gets or sets a provider-generated payload value restored after rollback.</summary>
        internal int GeneratedValue { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        internal Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        internal SingleIdentityKey? ParentId { get; set; }

        /// <summary>Gets or sets the left interval boundary.</summary>
        internal long Left { get; set; }

        /// <summary>Gets or sets the right interval boundary.</summary>
        internal long Right { get; set; }

        /// <summary>Gets or sets the zero-based depth.</summary>
        internal int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        internal long Position { get; set; }

        /// <summary>Gets or sets the owned payload.</summary>
        internal SingleIdentityDetails Details { get; set; } = new();

        /// <summary>Gets or sets a foreign navigation that single insertion must reject before tracking.</summary>
        internal SingleIdentityForeign? Foreign { get; set; }
    }

    /// <summary>Provides populated non-owned navigation input for early-rejection cleanup.</summary>
    private sealed class SingleIdentityForeign
    {
        /// <summary>Gets or sets its application identity.</summary>
        internal int Id { get; set; }
    }
}
