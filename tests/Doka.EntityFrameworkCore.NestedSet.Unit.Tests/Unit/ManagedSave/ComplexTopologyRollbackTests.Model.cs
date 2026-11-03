namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyRollbackTests
{
    /// <summary>Maps nullable complex containers and nested JSON lists without opening a database.</summary>
    internal class TopologyContext : DbContext
    {
        /// <summary>Leaves scalar and graph detection under the test's explicit control.</summary>
        internal TopologyContext() => ChangeTracker.AutoDetectChangesEnabled = false;

        /// <summary>Gets whether the payload is a required peer of a principal sharing its row.</summary>
        protected virtual bool Sharing => false;

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
            var payload = modelBuilder.Entity<TopologyPayload>();
            payload
                .Property(value => value.Id)
                .ValueGeneratedNever();
            payload
                .ComplexProperty(value => value.Optional)
                .IsRequired(false);
            payload.ComplexCollection(value => value.Rows,
                rows =>
                {
                    rows.ToJson("Rows");
                    rows
                        .ComplexProperty(value => value.Details)
                        .IsRequired(false);
                    rows.ComplexCollection(value => value.Leaves);
                });

            modelBuilder
                .Entity<UnrelatedPayload>()
                .Property(value => value.Id)
                .ValueGeneratedNever();

            if (Sharing)
            {
                payload.ToTable("TopologyRows");
                var owner = modelBuilder.Entity<TopologyOwner>();
                owner.ToTable("TopologyRows");
                owner
                    .Property(value => value.Id)
                    .ValueGeneratedNever();
                owner
                    .HasOne(value => value.Payload)
                    .WithOne(value => value.Owner)
                    .HasForeignKey<TopologyPayload>(value => value.Id)
                    .OnDelete(DeleteBehavior.Cascade);
                owner
                    .Navigation(value => value.Payload)
                    .IsRequired();
            }
            else
            {
                payload.Ignore(value => value.Owner);
                modelBuilder.Ignore<TopologyOwner>();
            }
        }
    }

    /// <summary>Uses the same complex shape on an actual required table-sharing peer.</summary>
    private sealed class SharedTopologyContext : TopologyContext
    {
        /// <inheritdoc />
        protected override bool Sharing => true;
    }

    /// <summary>Contains ordinary scalar storage and independently replaceable complex containers.</summary>
    internal sealed class TopologyPayload
    {
        /// <summary>Gets or sets the fixed tracked identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets ordinary payload with an independently captured original value.</summary>
        public string Marker { get; set; } = "Stored marker";

        /// <summary>Gets or sets the nullable JSON list whose exact instance belongs to the caller.</summary>
        public List<TopologyItem>? Rows { get; set; } = [];

        /// <summary>Gets or sets optional complex storage outside the JSON list.</summary>
        public TopologyDetails? Optional { get; set; } = new();

        /// <summary>Gets or sets the principal only in the table-sharing model.</summary>
        public TopologyOwner? Owner { get; set; }
    }

    /// <summary>Contains one direct scalar, an optional reference and an independently mutable nested list.</summary>
    internal sealed class TopologyItem
    {
        /// <summary>Gets or sets the element's ordinary scalar.</summary>
        public string Text { get; set; } = "Stored item";

        /// <summary>Gets or sets a nullable complex reference inside the JSON element.</summary>
        public TopologyDetails? Details { get; set; } = new();

        /// <summary>Gets or sets the exact nested list retained across rollback.</summary>
        public List<TopologyLeaf>? Leaves { get; set; } = [];
    }

    /// <summary>Provides a nested complex element with observable scalar and native element state.</summary>
    internal sealed class TopologyLeaf
    {
        /// <summary>Gets or sets the independently tracked nested scalar.</summary>
        public string Text { get; set; } = "Stored leaf";
    }

    /// <summary>Provides required existence data and optional payload for a nullable complex reference.</summary>
    internal sealed class TopologyDetails
    {
        /// <summary>Gets or sets the required existence-determining scalar.</summary>
        public string Text { get; set; } = "Stored details";

        /// <summary>Gets or sets independent nullable payload.</summary>
        public string? Code { get; set; } = "Stored code";
    }

    /// <summary>Owns the shared physical row without participating in the complex graph.</summary>
    internal sealed class TopologyOwner
    {
        /// <summary>Gets or sets the common row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets independently marked application payload.</summary>
        public string Name { get; set; } = "Stored owner";

        /// <summary>Gets or sets the required table-sharing peer.</summary>
        public TopologyPayload? Payload { get; set; }
    }

    /// <summary>Detects whether complex recovery failures prevent independent entries from restoring.</summary>
    private sealed class UnrelatedPayload
    {
        /// <summary>Gets or sets an identity unrelated to the complex payload's key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets raw application payload with independent original and modified state.</summary>
        public string Name { get; set; } = "Stored unrelated";
    }
}
