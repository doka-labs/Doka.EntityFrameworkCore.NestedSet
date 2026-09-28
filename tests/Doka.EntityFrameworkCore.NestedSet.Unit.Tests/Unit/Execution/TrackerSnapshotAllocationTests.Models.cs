namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TrackerSnapshotAllocationTests
{
    /// <summary>Uses a relational model without opening a connection during allocation measurement.</summary>
    internal sealed class SnapshotContext : DbContext
    {
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
                .Entity<SnapshotScalars>()
                .Property(value => value.Id)
                .ValueGeneratedNever();
            modelBuilder
                .Entity<SnapshotPayload>()
                .Property(value => value.Id)
                .ValueGeneratedNever();
            var values = modelBuilder
                .Entity<SnapshotPayload>()
                .Property(value => value.Values);

            values.HasConversion(
                value => string.Join(",", value),
                value => value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                    .ToList());

            values.Metadata.SetValueComparer(
                new ValueComparer<List<int>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    value => value.Aggregate(0, HashCode.Combine),
                    value => value.ToList()));
        }
    }

    /// <summary>Represents the fifteen ordinary scalar columns in the review's allocation scenario.</summary>
    internal sealed class SnapshotScalars
    {
        /// <summary>Gets or sets the explicit identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets scalar column 01.</summary>
        public int Value01 { get; set; }

        /// <summary>Gets or sets scalar column 02.</summary>
        public int Value02 { get; set; }

        /// <summary>Gets or sets scalar column 03.</summary>
        public int Value03 { get; set; }

        /// <summary>Gets or sets scalar column 04.</summary>
        public int Value04 { get; set; }

        /// <summary>Gets or sets scalar column 05.</summary>
        public int Value05 { get; set; }

        /// <summary>Gets or sets scalar column 06.</summary>
        public int Value06 { get; set; }

        /// <summary>Gets or sets scalar column 07.</summary>
        public int Value07 { get; set; }

        /// <summary>Gets or sets scalar column 08.</summary>
        public int Value08 { get; set; }

        /// <summary>Gets or sets scalar column 09.</summary>
        public int Value09 { get; set; }

        /// <summary>Gets or sets scalar column 10.</summary>
        public int Value10 { get; set; }

        /// <summary>Gets or sets scalar column 11.</summary>
        public int Value11 { get; set; }

        /// <summary>Gets or sets scalar column 12.</summary>
        public int Value12 { get; set; }

        /// <summary>Gets or sets scalar column 13.</summary>
        public int Value13 { get; set; }

        /// <summary>Gets or sets scalar column 14.</summary>
        public int Value14 { get; set; }
    }

    /// <summary>Uses a deep EF comparer for a caller-mutable mapped value.</summary>
    internal sealed class SnapshotPayload
    {
        /// <summary>Gets or sets the explicit identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets a mutable payload whose snapshot must remain independent.</summary>
        public List<int> Values { get; set; } = [];
    }
}
