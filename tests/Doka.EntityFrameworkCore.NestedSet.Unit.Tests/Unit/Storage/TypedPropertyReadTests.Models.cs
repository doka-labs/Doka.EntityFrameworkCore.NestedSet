using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides model-only fixtures for live typed value and optional parent reads.</summary>
public sealed partial class TypedPropertyReadTests
{
    /// <summary>Retains only the mapped metadata reused by the allocation measurement.</summary>
    private sealed class ReadProperties
    {
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ReadProperties(
            IEntityType entityType
        )
        {
            IntegerParent = entityType.FindProperty(nameof(ReadNode.IntegerParent))!;
            StructParent = entityType.FindProperty(nameof(ReadNode.StructParent))!;
            Left = entityType.FindProperty(nameof(ReadNode.Left))!;
            Right = entityType.FindProperty(nameof(ReadNode.Right))!;
            Depth = entityType.FindProperty(nameof(ReadNode.Depth))!;
            Position = entityType.FindProperty(nameof(ReadNode.Position))!;
        }

        /// <summary>Gets the optional integer parent metadata.</summary>
        internal IProperty IntegerParent { get; }

        /// <summary>Gets the optional converted value parent metadata.</summary>
        internal IProperty StructParent { get; }

        /// <summary>Gets the left boundary metadata.</summary>
        internal IProperty Left { get; }

        /// <summary>Gets the right boundary metadata.</summary>
        internal IProperty Right { get; }

        /// <summary>Gets the depth metadata.</summary>
        internal IProperty Depth { get; }

        /// <summary>Gets the sibling position metadata.</summary>
        internal IProperty Position { get; }
    }

    /// <summary>Keeps the optional runtime nullable-key result observable by the test.</summary>
    /// <param name="HasValue">Whether the current parent is present.</param>
    /// <param name="Value">The observable parent identity or null.</param>
    private readonly record struct ParentProbe(
        bool HasValue,
        object? Value
    );

    /// <summary>Provides scalar properties, named indexers and generated keys without hierarchy scaffolding.</summary>
    private sealed class ReadContext : DbContext
    {
        /// <summary>Names the optional shadow parent.</summary>
        internal const string ShadowParent = "ShadowParent";

        /// <summary>Names the dictionary entity mapping.</summary>
        internal const string SharedEntity = "TypedPropertyReadShared";

        /// <summary>Names the required dictionary key.</summary>
        internal const string SharedKey = "Id";

        /// <summary>Names the optional dictionary parent.</summary>
        internal const string SharedParent = "ParentId";

        /// <summary>Creates a model-only context with automatic detection disabled for explicit live reads.</summary>
        internal ReadContext()
            : base(
                new DbContextOptionsBuilder<ReadContext>()
                    .ConfigureTestWarnings()
                    .UseSqlite("Data Source=:memory:")
                    .Options)
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<ReadNode>();
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node.Property<int?>(ShadowParent);

            var valueConverter =
                new ValueConverter<ValueKey, long>(
                    value => value.Value,
                    value => new ValueKey(value));

            var referenceConverter =
                new ValueConverter<ReferenceKey?, string?>(
                    value => ToProvider(value),
                    value => FromProvider(value));

            node
                .Property(value => value.StructParent)
                .HasConversion(valueConverter);

            node
                .Property(value => value.ConvertedReferenceParent)
                .HasConversion(referenceConverter);

            var nullable = modelBuilder.Entity<NullableKeyNode>();
            nullable.HasKey(value => value.Id);
            nullable
                .Property(value => value.Id)
                .IsRequired()
                .ValueGeneratedNever();

            modelBuilder.Entity<TemporaryKeyNode>();

            var shared = modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(SharedEntity);
            shared
                .IndexerProperty<int>(SharedKey)
                .ValueGeneratedNever();

            shared.IndexerProperty<int?>(SharedParent);
            shared.HasKey(SharedKey);
        }

        /// <summary>Converts optional reference storage without invoking its misleading domain operators.</summary>
        private static string? ToProvider(
            ReferenceKey? value
        ) => value?.Value;

        /// <summary>Restores optional provider storage without replacing a missing parent with a default key.</summary>
        private static ReferenceKey? FromProvider(
            string? value
        ) => value is null
            ? null
            : new ReferenceKey(value);
    }

    /// <summary>Provides an operator-free converted value key.</summary>
    private readonly struct ValueKey : IEquatable<ValueKey>
    {
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ValueKey(
            long value
        )
        {
            Value = value;
        }

        /// <summary>Gets the stored scalar identity.</summary>
        public long Value { get; }

        /// <inheritdoc />
        public bool Equals(
            ValueKey other
        ) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(
            object? obj
        ) => obj is ValueKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>Exposes misleading domain operators to exercise reference identity checks for null.</summary>
    private sealed class ReferenceKey : IEquatable<ReferenceKey>
    {
        /// <summary>Initializes the test probe with its explicit dependencies.</summary>
        public ReferenceKey(
            string value
        )
        {
            Value = value;
        }

        /// <summary>Gets the stored reference identity.</summary>
        public string Value { get; }

        /// <inheritdoc />
        public bool Equals(
            ReferenceKey? other
        ) => other is not null && Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(
            object? obj
        ) => obj is ReferenceKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        /// <summary>Deliberately reports every pair equal; presence must not invoke this domain operator.</summary>
        public static bool operator==(
            ReferenceKey? first,
            ReferenceKey? second
        )
        {
            return true;
        }

        /// <summary>Deliberately hides all inequality; parent presence must ignore this domain operator.</summary>
        public static bool operator!=(
            ReferenceKey? first,
            ReferenceKey? second
        )
        {
            return false;
        }
    }

    /// <summary>Exposes ordinary structural values and all supported optional parent shapes.</summary>
    private sealed class ReadNode
    {
        /// <summary>Gets or sets the assigned row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional integer parent.</summary>
        public int? IntegerParent { get; set; }

        /// <summary>Gets or sets the optional Guid parent.</summary>
        public Guid? GuidParent { get; set; }

        /// <summary>Gets or sets the optional string parent.</summary>
        public string? ReferenceParent { get; set; }

        /// <summary>Gets or sets the optional converted reference parent.</summary>
        public ReferenceKey? ConvertedReferenceParent { get; set; }

        /// <summary>Gets or sets the optional operator-free value parent.</summary>
        public ValueKey? StructParent { get; set; }

        /// <summary>Gets or sets the left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Retains EF's supported required key with nullable CLR storage.</summary>
    private sealed class NullableKeyNode
    {
        /// <summary>Gets or sets the required identity with nullable CLR storage.</summary>
        public int? Id { get; set; }

        /// <summary>Gets or sets the optional parent using the same CLR storage shape.</summary>
        public int? ParentId { get; set; }
    }

    /// <summary>Uses conventional temporary integer key generation.</summary>
    private sealed class TemporaryKeyNode
    {
        /// <summary>Gets or sets the generated row identity.</summary>
        public int Id { get; set; }
    }
}
