using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexContainerSemanticsRecoveryTests
{
    /// <summary>Maps each container semantics with automatic detection off, as a suspended callback sees it.</summary>
    private sealed class SemanticsContext : DbContext
    {
        /// <summary>Prevents observation from implicitly accepting callback changes.</summary>
        internal SemanticsContext()
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
            modelBuilder
                .Entity<GuardedPayload>()
                .ComplexCollection(value => value.Rows, rows => rows.ToJson("Rows"));

            modelBuilder
                .Entity<ObservablePayload>()
                .ComplexCollection(value => value.Rows, rows => rows.ToJson("Rows"));

            modelBuilder
                .Entity<EqualityPayload>()
                .ComplexProperty(value => value.Value);

            modelBuilder
                .Entity<NullablePayload>()
                .ComplexProperty(
                    value => value.Value,
                    value => value.PrimitiveCollection(item => item.Tags));

            modelBuilder
                .Entity<NullableRowsPayload>()
                .ComplexCollection(
                    value => value.Rows,
                    rows =>
                    {
                        rows.ToJson("Rows");
                        rows.ComplexProperty(row => row.Value, value => value.PrimitiveCollection(item => item.Tags));
                    });

            modelBuilder
                .Entity<PropertyGuardedPayload>()
                .ComplexCollection(
                    value => value.Rows,
                    rows =>
                    {
                        rows.ToJson("Rows");
                        rows.UsePropertyAccessMode(PropertyAccessMode.Property);
                    });

            modelBuilder
                .Entity<ViewPayload>()
                .ComplexCollection(value => value.Rows, rows => rows.ToJson("Rows"));

            modelBuilder
                .Entity<CopyingPayload>()
                .ComplexCollection(
                    value => value.Rows,
                    rows =>
                    {
                        rows.ToJson("Rows");
                        rows.UsePropertyAccessMode(PropertyAccessMode.Property);
                    });
        }
    }

    /// <summary>Rejects null membership and counts every caller-visible mutation.</summary>
    private sealed class GuardedRows : Collection<Row>
    {
        /// <summary>Gets or sets the number of mutator calls since the last reset.</summary>
        public int Mutations { get; set; }

        /// <inheritdoc />
        protected override void InsertItem(
            int index,
            Row item
        )
        {
            ArgumentNullException.ThrowIfNull(item);
            Mutations++;
            base.InsertItem(index, item);
        }

        /// <inheritdoc />
        protected override void ClearItems()
        {
            Mutations++;
            base.ClearItems();
        }
    }

    /// <summary>Provides a complex collection element.</summary>
    private sealed class Row
    {
        /// <summary>Gets or sets element payload.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Holds a validating collection behind an auto-property's backing field.</summary>
    private sealed class GuardedPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the field-mapped collection.</summary>
        public GuardedRows Rows { get; set; } = [];
    }

    /// <summary>Holds a validating collection behind a property-mode setter.</summary>
    private sealed class PropertyGuardedPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the property-mapped collection.</summary>
        public GuardedRows Rows { get; set; } = [];
    }

    /// <summary>Declares a mutable collection whose runtime value can be a read-only view.</summary>
    private sealed class ViewPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the mapped collection.</summary>
        public IList<Row> Rows { get; set; } = [];
    }

    /// <summary>Copies every assigned list, so assigning can never keep an instance.</summary>
    private sealed class CopyingPayload
    {
        private List<Row> _store = [];

        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets the stored list or copies an assigned one.</summary>
        public List<Row> Rows
        {
            get => _store;
            set => _store = [.. value];
        }
    }

    /// <summary>Holds an observable collection behind an auto-property's backing field.</summary>
    private sealed class ObservablePayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the field-mapped collection.</summary>
        public ObservableCollection<Row> Rows { get; set; } = [];
    }

    /// <summary>Holds a struct whose equality deliberately ignores one member.</summary>
    private sealed class EqualityPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the value-type complex reference.</summary>
        public EqualityValue Value { get; set; }
    }

    /// <summary>Compares only its mapped key.</summary>
    private struct EqualityValue : IEquatable<EqualityValue>
    {
        /// <summary>Gets or sets the mapped scalar.</summary>
        public int Key { get; set; }

        /// <summary>Gets or sets an unmapped member that recovery still treats as part of the box.</summary>
        [NotMapped]
        public int Note { get; set; }

        /// <inheritdoc />
        public readonly bool Equals(
            EqualityValue other
        ) => Key == other.Key;

        /// <inheritdoc />
        public override readonly bool Equals(
            object? obj
        ) => obj is EqualityValue other && Equals(other);

        /// <inheritdoc />
        public override readonly int GetHashCode() => Key;
    }

    /// <summary>Holds a nullable struct reference.</summary>
    private sealed class NullablePayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the nullable value-type complex reference.</summary>
        public NullableValue? Value { get; set; }
    }

    /// <summary>Holds collection elements that contain nullable struct references.</summary>
    private sealed class NullableRowsPayload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the complex collection.</summary>
        public List<NullableRow> Rows { get; set; } = [];
    }

    /// <summary>Contains a nullable struct reference.</summary>
    private sealed class NullableRow
    {
        /// <summary>Gets or sets the nullable value-type complex reference.</summary>
        public NullableValue? Value { get; set; }
    }

    /// <summary>Provides a scalar and a mutable primitive collection.</summary>
    private struct NullableValue
    {
        /// <summary>Gets or sets the mapped scalar.</summary>
        public int Count { get; set; }

        /// <summary>Gets or sets the mapped primitive collection.</summary>
        public List<string>? Tags { get; set; }
    }
}
