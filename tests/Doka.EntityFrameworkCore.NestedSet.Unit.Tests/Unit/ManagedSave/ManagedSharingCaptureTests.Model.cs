using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ManagedSharingCaptureTests
{
    /// <summary>Maps ordinary payload entities through real relational sharing and non-sharing shapes.</summary>
    internal sealed class SharingContext : DbContext
    {
        /// <summary>Leaves all changes explicit so closure discovery cannot hide an EF detection pass.</summary>
        internal SharingContext()
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
            var root = modelBuilder.Entity<SharingRoot>();
            root.ToTable("SharingRows");
            root
                .Property(value => value.Id)
                .ValueGeneratedNever();
            root
                .HasDiscriminator<string>("Kind")
                .HasValue<SharingRoot>("Root")
                .HasValue<DerivedSharingRoot>("Derived");

            root.OwnsOne(value => value.Details,
                details =>
                {
                    details.OwnsOne(value => value.Nested);
                    details
                        .Navigation(value => value.Nested)
                        .IsRequired();
                });

            root
                .Navigation(value => value.Details)
                .IsRequired();
            root.OwnsOne(value => value.Separate, separate => separate.ToTable("SeparatePayloads"));
            root
                .HasOne(value => value.Shared)
                .WithOne(value => value.Root)
                .HasForeignKey<SharingPart>(value => value.Id)
                .OnDelete(DeleteBehavior.Cascade);

            var shared = modelBuilder.Entity<SharingPart>();
            shared.ToTable("SharingRows");
            shared
                .Property(value => value.Id)
                .ValueGeneratedNever();
            shared
                .Property(value => value.Payload)
                .HasColumnName("SplitPayload");
            var related = modelBuilder.Entity<RelatedPayload>();
            related.ToTable("RelatedPayloads");
            related
                .Property(value => value.Id)
                .ValueGeneratedNever();
            related
                .HasOne(value => value.Root)
                .WithMany(value => value.Related)
                .HasForeignKey(value => value.RootId)
                .OnDelete(DeleteBehavior.Restrict);

            var json = modelBuilder.Entity<JsonOwner>();
            json.ToTable("JsonRows");
            json
                .Property(value => value.Id)
                .ValueGeneratedNever();
            // WHY: EF forbids table splitting for a JSON owner. Keep this negative control on a valid,
            // independent row instead of combining unsupported mapping shapes in the sharing fixture.
            json.OwnsOne(value => value.Document, document => document.ToJson("Document"));
        }
    }

    /// <summary>Creates an independent physical row with both shared and deliberately non-shared payload.</summary>
    internal static SharingRoot CreateRoot(
        int id,
        bool derived = false
    )
    {
        var root = derived ? new DerivedSharingRoot() : new SharingRoot();
        root.Id = id;
        root.Payload = "Root payload";
        root.Details = new SharingDetails
        {
            Payload = "Owned payload",
            Nested = new NestedSharingDetails { Payload = "Nested payload" },
        };

        root.Shared = new SharingPart
        {
            Id = id,
            Root = root,
            Payload = "Split payload",
        };
        root.Separate = new SeparatePayload { Payload = "Separate payload" };
        root.Related.Add(new RelatedPayload
        {
            Id = id,
            RootId = id,
            Root = root,
            Payload = "Related payload",
        });

        return root;
    }

    /// <summary>Returns the exact four native entries that share this one physical relational row.</summary>
    internal static IUpdateEntry[] SharedEntries(
        SharingContext context,
        SharingRoot root
    )
        =>
        [
            Entry(context, root), Entry(context, root.Details), Entry(context, root.Details.Nested),
            Entry(context, root.Shared),
        ];

    /// <summary>Reads an existing native identity without detecting changes or creating a new entity state.</summary>
    internal static IUpdateEntry Entry(
        DbContext context,
        object entity
    )
        => NestedSetInsertionContract.Instance.Entry(context.Entry(entity));

    /// <summary>Owns the main table row and unrelated relational payload through distinct mappings.</summary>
    internal class SharingRoot
    {
        /// <summary>Gets or sets the independent physical row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets ordinary payload used to select one actual callback write.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets or sets owned payload sharing this exact table row.</summary>
        public SharingDetails Details { get; set; } = new();

        /// <summary>Gets or sets an independent entity sharing the same primary-key row.</summary>
        public SharingPart Shared { get; set; } = null!;

        /// <summary>Gets or sets owned payload stored in a separate physical table.</summary>
        public SeparatePayload Separate { get; set; } = new();

        /// <summary>Gets ordinary related rows that must remain outside physical row-sharing closure.</summary>
        public List<RelatedPayload> Related { get; } = [];
    }

    /// <summary>Exercises an inherited row mapping while ownership remains declared on the base type.</summary>
    internal sealed class DerivedSharingRoot : SharingRoot
    {
        /// <summary>Gets or sets an unrelated inherited-row payload column.</summary>
        public string DerivedPayload { get; set; } = string.Empty;
    }

    /// <summary>Shares its ownership principal's table and owns another same-row payload entry.</summary>
    internal sealed class SharingDetails
    {
        /// <summary>Gets or sets the owned payload column.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets or sets nested payload stored in the same physical row.</summary>
        public NestedSharingDetails Nested { get; set; } = new();
    }

    /// <summary>Requires transitive closure rather than one direct ownership lookup.</summary>
    internal sealed class NestedSharingDetails
    {
        /// <summary>Gets or sets the nested owned payload column.</summary>
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>Shares a physical row without EF ownership.</summary>
    internal sealed class SharingPart
    {
        /// <summary>Gets or sets both the primary key and same-row identifying FK.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the table-split payload column.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets or sets the independently tracked principal sharing the table row.</summary>
        public required SharingRoot Root { get; set; }
    }

    /// <summary>Is owned but deliberately stored outside its principal's table.</summary>
    internal sealed class SeparatePayload
    {
        /// <summary>Gets or sets a column belonging only to the separate physical row.</summary>
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>Owns a JSON document without sharing its physical table row with another entity.</summary>
    internal sealed class JsonOwner
    {
        /// <summary>Gets or sets the independent JSON owner's row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets an ordinary owner column independent of its JSON document.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets or sets a JSON-owned object that has no independently promoted table entry.</summary>
        public JsonPayload Document { get; set; } = new();
    }

    /// <summary>Is owned through a JSON document rather than an independent table entry.</summary>
    internal sealed class JsonPayload
    {
        /// <summary>Gets or sets the JSON payload member.</summary>
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>Uses an ordinary FK that must not be mistaken for physical row sharing.</summary>
    internal sealed class RelatedPayload
    {
        /// <summary>Gets or sets this independently stored row's primary key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the regular FK to the sharing root.</summary>
        public int RootId { get; set; }

        /// <summary>Gets or sets ordinary independently stored payload.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets or sets the regular principal reference.</summary>
        public required SharingRoot Root { get; set; }
    }
}
