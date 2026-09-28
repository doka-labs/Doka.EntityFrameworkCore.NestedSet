namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTrackerTests
{
    /// <summary>Combines ordered nodes with unrelated generated identities and required relationships.</summary>
    private sealed class TrackerContext : NestedSetDbContext
    {
        /// <summary>Creates the coordinated context using the isolated relational database.</summary>
        internal TrackerContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureNode(modelBuilder, "TrackerNodes", this);
            modelBuilder
                .Entity<TrackerNode>()
                .HasNestedSet(builder => builder
                    .HasTreeId(node => node.TreeId)
                    .HasScope(node => node.Scope)
                    .HasParent(node => node.ParentId)
                    .OrderBy(node => node.Name));
            modelBuilder
                .Entity<TrackerAddition>()
                .ToTable("TrackerAdditions");
            modelBuilder
                .Entity<TrackerAddition>()
                .Property(addition => addition.Id)
                .ValueGeneratedOnAdd();
            modelBuilder
                .Entity<TrackerOwner>()
                .ToTable("TrackerOwners")
                .Property(owner => owner.Id)
                .ValueGeneratedNever();
            modelBuilder
                .Entity<TrackerChild>()
                .ToTable("TrackerChildren")
                .Property(child => child.Id)
                .ValueGeneratedNever();
            modelBuilder
                .Entity<TrackerChild>()
                .HasOne(child => child.Owner)
                .WithMany(owner => owner.Children)
                .HasForeignKey(child => child.OwnerId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>Provides ordinary EF behavior as the reference for unaccepted generated concurrency values.</summary>
    private sealed class PlainTrackerContext : DbContext
    {
        /// <summary>Creates a context that shares the provider but has no save coordinator.</summary>
        internal PlainTrackerContext(
            DbContextOptions options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => ConfigureNode(modelBuilder, "PlainTrackerNodes", this);
    }

    /// <summary>Maps equivalent computed concurrency properties for coordinated and ordinary EF contexts.</summary>
    private static void ConfigureNode(
        ModelBuilder modelBuilder,
        string table,
        DbContext context
    )
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var node = modelBuilder.Entity<TrackerNode>();
        node.ToTable(table);
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();
        node
            .Property(value => value.Name)
            .HasMaxLength(128)
            .IsRequired();

        // WHY: Deterministic computed values expose generated-value handling without timing or random tokens.
        node
            .Property(value => value.NameToken)
            .HasMaxLength(128)
            .HasComputedColumnSql(sql.DelimitIdentifier(nameof(TrackerNode.Name)), stored: true)
            .IsConcurrencyToken();

        var position = sql.DelimitIdentifier(nameof(TrackerNode.Position));
        var coordinateExpression = context.Database.IsSqlServer()
            ? $"CAST({position} AS int) + 100"
            : position + " + 100";

        node
            .Property(value => value.CoordinateToken)
            .HasComputedColumnSql(coordinateExpression, stored: true)
            .IsConcurrencyToken();
    }

    /// <summary>Contains payload and structural concurrency tokens that change for different reasons.</summary>
    private sealed class TrackerNode : IScopedNestedSetNode<int, Guid, int>
    {
        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <inheritdoc />
        public int Id { get; set; }

        /// <summary>Gets or sets the isolated forest.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the direct parent.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the ascending sibling sort value.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets the database-generated payload concurrency value.</summary>
        public string NameToken { get; set; } = "";

        /// <summary>Gets or sets the database-generated structural concurrency value.</summary>
        public int CoordinateToken { get; set; }

        /// <inheritdoc />
        public long Left { get; set; }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }
    }

    /// <summary>Exposes CLR sentinel and temporary tracker identity restoration after a rolled-back insert.</summary>
    private sealed class TrackerAddition
    {
        /// <summary>Gets or sets the identity generated by the database.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the unrelated pending payload.</summary>
        public string Value { get; set; } = "pending";
    }

    /// <summary>Owns a required child whose deletion can be deferred until SaveChanges.</summary>
    private sealed class TrackerOwner
    {
        /// <summary>Gets or sets the explicit owner identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets children whose removal creates a required orphan.</summary>
        public List<TrackerChild> Children { get; } = [];
    }

    /// <summary>Represents an unrelated required dependent participating in the same atomic save.</summary>
    private sealed class TrackerChild
    {
        /// <summary>Gets or sets the explicit child identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the required persisted owner identity.</summary>
        public int OwnerId { get; set; }

        /// <summary>Gets or sets the owner navigation, which can be absent for a pending orphan.</summary>
        public TrackerOwner? Owner { get; set; }
    }
}
