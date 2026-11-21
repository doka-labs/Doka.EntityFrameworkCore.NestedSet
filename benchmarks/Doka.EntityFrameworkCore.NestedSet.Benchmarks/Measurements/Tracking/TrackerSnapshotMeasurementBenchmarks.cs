namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>
/// Observes complete tracker capture for the original twenty-thousand-row, fifteen-scalar unit of work.
/// </summary>
public class TrackerSnapshotMeasurementBenchmarks : IDisposable
{
    private SnapshotContext _context = null!;

    /// <summary>Releases owned fixture resources independently of semantic verification.</summary>
    public void Dispose()
    {
        try
        {
            Dispose(disposing: true);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Releases managed fixture resources once, including failed or incomplete setup.</summary>
    /// <param name="disposing">Whether this call owns managed-resource cleanup.</param>
    protected virtual void Dispose(
        bool disposing
    )
    {
        if (!disposing)
        {
            return;
        }

        var context = _context;
        _context = null!;
        context?.Dispose();
    }

    /// <summary>Gets or sets the independently retained tracked population.</summary>
    [Params(20_000)]
    public int Entries { get; set; } = 20_000;

    /// <summary>Attaches the unchanged scalar rows before BenchmarkDotNet starts its measurements.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new SnapshotContext();
        _context.AttachRange(Enumerable.Range(1, Entries).Select(id => new SnapshotScalars { Id = id }));
        VerifyWorkload();
    }

    /// <summary>Captures one complete production tracker snapshot.</summary>
    /// <returns>The immutable capture retained by the benchmark consumer.</returns>
    [Benchmark]
    public object Capture() => NestedSetTrackerSnapshot.Capture(_context);

    /// <summary>Requires all independently attached rows and scalar columns to remain present.</summary>
    public void VerifyWorkload()
    {
        var metadata = _context.Model.FindEntityType(typeof(SnapshotScalars))!;
        if (_context.ChangeTracker.Entries().Count() != Entries || metadata.GetProperties().Count() != 15)
        {
            throw new InvalidOperationException("The tracked scalar snapshot workload changed.");
        }
    }

    /// <summary>Releases all fixture-owned entries and services after verification.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            VerifyWorkload();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Uses a relational model without opening a connection during allocation measurement.</summary>
    internal sealed class SnapshotContext : TrackingMeasurementContext<SnapshotContext>
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

    /// <summary>Represents the fifteen ordinary scalar columns in the original scalar-capture scenario.</summary>
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
