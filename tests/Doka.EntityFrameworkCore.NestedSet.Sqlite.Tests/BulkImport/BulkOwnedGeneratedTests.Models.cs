namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkOwnedGeneratedTests
{
    /// <summary>Maps separate generated owned rows together with a table-sharing generated owned payload.</summary>
    private sealed class OwnedGeneratedContext
        : NestedSetDbContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public OwnedGeneratedContext(
            DbContextOptions<OwnedGeneratedContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            var node = builder.Entity<OwnedGeneratedNode>();
            node.ToTable("OwnedGeneratedImportNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Revision)
                .HasDefaultValue(42);

            node.HasNestedSet(set => set
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .HasBounds(value => value.Left, value => value.Right)
                .HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));

            node.OwnsOne(
                value => value.Payload,
                payload =>
                {
                    payload
                        .WithOwner()
                        .HasForeignKey(value => value.OwnerId);

                    payload
                        .Property(value => value.OwnerId)
                        .ValueGeneratedNever();

                    payload
                        .Property(value => value.Revision)
                        .HasDefaultValue(43);

                    payload
                        .HasOne<OwnedReference>()
                        .WithMany()
                        .HasForeignKey(value => value.ReferenceId);
                });

            node.OwnsMany(
                value => value.Rows,
                rows =>
                {
                    rows.ToTable("OwnedGeneratedImportRows");
                    rows
                        .WithOwner()
                        .HasForeignKey(value => value.OwnerId);

                    rows
                        .Property(value => value.OwnerId)
                        .ValueGeneratedNever();

                    rows.HasKey(value => value.Id);
                    rows
                        .Property(value => value.Id)
                        .ValueGeneratedOnAdd();

                    rows
                        .Property(value => value.Revision)
                        .HasDefaultValue(43);
                });
        }
    }

    /// <summary>Provides caller-owned root structure and a generated default.</summary>
    private sealed class OwnedGeneratedNode
    {
        /// <summary>Gets or sets the assigned node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left nested-set boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right nested-set boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth relative to the root.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the database-generated root default.</summary>
        public int Revision { get; set; }

        /// <summary>Gets or sets the generated table-sharing owned payload.</summary>
        public OwnedGeneratedPayload Payload { get; set; } = new();

        /// <summary>Gets or sets the owned collection containing database-generated identities.</summary>
        public List<OwnedGeneratedRow> Rows { get; set; } = [];
    }

    /// <summary>Provides a database-generated non-key default within a table-sharing owned entity.</summary>
    private sealed class OwnedGeneratedPayload
    {
        /// <summary>Gets or sets the public ownership foreign and primary identity.</summary>
        public int OwnerId { get; set; }

        /// <summary>Gets or sets an ordinary business foreign key controlled by application callbacks.</summary>
        public int? ReferenceId { get; set; }

        /// <summary>Gets or sets the generated owned default.</summary>
        public int Revision { get; set; }

        /// <summary>Gets or sets ordinary caller-owned payload.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Provides both a generated owned collection identity and a generated ordinary scalar.</summary>
    private sealed class OwnedGeneratedRow
    {
        /// <summary>Gets or sets the public ownership foreign identity.</summary>
        public int OwnerId { get; set; }

        /// <summary>Gets or sets the generated owned-row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the generated owned-row default.</summary>
        public int Revision { get; set; }

        /// <summary>Gets or sets ordinary caller-owned row payload.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Provides a business principal whose foreign key is separate from aggregate ownership.</summary>
    private sealed class OwnedReference
    {
        /// <summary>Gets or sets the business principal identity.</summary>
        public int Id { get; set; }
    }
}
