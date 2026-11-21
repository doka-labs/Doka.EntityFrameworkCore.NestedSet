using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class TypedParentContractTests
{
    /// <summary>An immutable converted scalar supplies typed equality without equality operators.</summary>
    private readonly struct OperatorFreeKey : IEquatable<OperatorFreeKey>
    {
        /// <summary>Retains the supplied fixture values without changing their identity.</summary>
        /// <param name="value">The model value retained by this fixture identity.</param>
        public OperatorFreeKey(
            int value
        )
        {
            Value = value;
        }

        /// <summary>Gets the native integer identity persisted by the model converter.</summary>
        public int Value { get; }

        /// <inheritdoc />
        public bool Equals(
            OperatorFreeKey other
        ) => Value.Equals(other.Value);

        /// <inheritdoc />
        public override bool Equals(
            object? other
        ) => other is OperatorFreeKey key && Equals(key);

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>Represents an assigned converted key whose nullable parent distinguishes zero from absence.</summary>
    private sealed class OperatorFreeNode : IScopedNestedSetNode<OperatorFreeKey, Guid, int>
    {
        /// <summary>Gets or sets the explicitly optional converted parent key.</summary>
        public OperatorFreeKey? ParentId { get; set; }

        /// <inheritdoc />
        public OperatorFreeKey Id { get; set; }

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Scope { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Maps operator-free key and parent values through the same bounded native integer type.</summary>
    private sealed class OperatorFreeContext : DbContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public OperatorFreeContext(
            DbContextOptions<OperatorFreeContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<OperatorFreeNode>();
            var converter =
                new ValueConverter<OperatorFreeKey, int>(
                    key => key.Value,
                    value => new OperatorFreeKey(value));

            node.ToTable("OperatorFreeTypedParentNodes");
            node.HasKey(value => value.Id);
            node
                .Property(value => value.Id)
                .HasConversion(converter)
                .ValueGeneratedNever();

            node
                .Property(value => value.ParentId)
                .HasConversion(converter);

            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));
        }
    }
}
