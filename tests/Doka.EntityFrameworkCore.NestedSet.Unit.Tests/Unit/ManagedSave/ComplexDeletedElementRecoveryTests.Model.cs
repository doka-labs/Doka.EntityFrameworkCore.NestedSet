namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexDeletedElementRecoveryTests
{
    /// <summary>Creates a payload around one retained first-level element.</summary>
    private static Payload NewPayload(
        int id,
        Outer row
    ) => new()
    {
        Id = id,
        Marker = "Stored",
        Rows = [row],
    };

    /// <summary>Creates a first-level element with an exact nested shape.</summary>
    private static Outer NewOuter(
        string name,
        int middles,
        int tips
    ) => new()
    {
        Name = name,
        Middles = Enumerable
            .Range(0, middles)
            .Select(index => NewMiddle(name + " " + index, tips))
            .ToList(),
    };

    /// <summary>Creates a second-level element with an exact number of third-level elements.</summary>
    private static Middle NewMiddle(
        string name,
        int tips
    ) => new()
    {
        Name = name,
        Tips = Enumerable
            .Range(0, tips)
            .Select(index => new Tip { Name = name + " tip " + index })
            .ToList(),
    };

    /// <summary>Maps three complex collection levels, the depth at which EF's Deleted recursion appears.</summary>
    private sealed class DeletedContext(
        SqliteConnection connection,
        bool ownsConnection
    ) : DbContext
    {
        /// <summary>Releases the in-memory store with the context that outlives the seeding context.</summary>
        public override void Dispose()
        {
            base.Dispose();

            if (ownsConnection)
            {
                connection.Dispose();
            }
        }

        /// <summary>Releases the in-memory store with the context that outlives the seeding context.</summary>
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();

            if (ownsConnection)
            {
                await connection.DisposeAsync();
            }
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
            => optionsBuilder.UseSqlite(connection);

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var payload = modelBuilder.Entity<Payload>();
            payload.ToTable("Payload");
            payload
                .Property(value => value.Id)
                .ValueGeneratedNever();

            payload.ComplexCollection(
                value => value.Rows,
                rows =>
                {
                    rows.ToJson("Rows");
                    rows.ComplexProperty(value => value.Data);
                    rows.ComplexCollection(
                        value => value.Middles,
                        middles => middles.ComplexCollection(value => value.Tips));
                });
        }
    }

    /// <summary>Contains the outermost complex collection and an independent scalar.</summary>
    private sealed class Payload
    {
        /// <summary>Gets or sets the fixed identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets independent application payload.</summary>
        public string Marker { get; set; } = string.Empty;

        /// <summary>Gets or sets the first complex collection level.</summary>
        public List<Outer>? Rows { get; set; }
    }

    /// <summary>Contains a value-type reference and the second complex collection level.</summary>
    private sealed class Outer
    {
        /// <summary>Gets or sets element payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets a value-type reference without a tracked complex original.</summary>
        public Measure Data { get; set; }

        /// <summary>Gets or sets the second complex collection level.</summary>
        public List<Middle>? Middles { get; set; }
    }

    /// <summary>Contains the third complex collection level.</summary>
    private sealed class Middle
    {
        /// <summary>Gets or sets element payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the third complex collection level.</summary>
        public List<Tip>? Tips { get; set; }
    }

    /// <summary>Terminates the mapped complex collection depth.</summary>
    private sealed class Tip
    {
        /// <summary>Gets or sets element payload.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Provides a mapped struct reference inside collection elements.</summary>
    private struct Measure
    {
        /// <summary>Gets or sets the mapped scalar.</summary>
        public int Value { get; set; }
    }
}
