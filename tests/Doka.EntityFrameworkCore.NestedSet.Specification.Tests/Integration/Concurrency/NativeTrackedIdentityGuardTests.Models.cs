namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Maps native identity equality independently from the model's value comparer.</summary>
    private abstract class GuardTreeContext : DbContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        protected GuardTreeContext(
            DbContextOptions options
        ) : base(options) { }

        /// <summary>Chooses the store's native case comparison for this concrete test model.</summary>
        protected abstract bool CaseInsensitive { get; }

        /// <summary>Chooses the intentionally broader model comparer for the positive control.</summary>
        protected abstract bool BroadComparer { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            // WHY: Each concrete model creates its own tables in the shared fixture database. Distinct
            // collation names preserve identical CI semantics without duplicate CREATE COLLATION statements.
            var postgresCollation = GetType()
                    .Name
                    .ToLowerInvariant()
                + "_tree_ci";

            if (CaseInsensitive && Database.IsNpgsql())
            {
                modelBuilder.HasCollation(
                    postgresCollation,
                    locale: "und-u-ks-level2",
                    provider: "icu",
                    deterministic: false);
            }

            var collation = CaseInsensitive
                ? Database.IsSqlite()
                    ? "NOCASE"
                    : Database.IsNpgsql()
                        ? postgresCollation
                        : Database.IsSqlServer()
                            ? "Latin1_General_100_CI_AS"
                            : "utf8mb4_general_ci"
                : Database.IsSqlite()
                    ? "BINARY"
                    : Database.IsSqlServer()
                        ? "Latin1_General_100_BIN2"
                        : Database.IsNpgsql()
                            ? null
                            : "utf8mb4_bin";

            var node = modelBuilder.Entity<BroadTreeIdNode>();
            node.ToTable(
                GetType()
                    .Name
                + "Nodes");

            node.HasKey(value => value.Id);
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Name)
                .HasMaxLength(100);

            var tree = node
                .Property(value => value.TreeId)
                .HasConversion(value => value.Value, value => new BroadTreeId(value))
                .HasMaxLength(40);

            if (collation is not null)
            {
                tree.UseCollation(collation);
            }

            // WHY: The negative control needs narrow CLR equality over a native CI column; the positive control
            // intentionally reverses that relationship. Neither comparer can stand in for database identity.
            tree.Metadata.SetValueComparer(
                BroadComparer
                    ? new ValueComparer<BroadTreeId>(
                        (first,
                            second) => StringComparer.OrdinalIgnoreCase.Equals(first!.Value, second!.Value),
                        value => StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value),
                        value => new BroadTreeId(value.Value))
                    : new ValueComparer<BroadTreeId>(
                        (first,
                            second) => StringComparer.Ordinal.Equals(first!.Value, second!.Value),
                        value => StringComparer.Ordinal.GetHashCode(value.Value),
                        value => new BroadTreeId(value.Value)));

            node
                .HasOne<BroadTreeIdNode>()
                .WithMany()
                .HasForeignKey(value => value.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            node.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(value => value.Id)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .HasBounds(value => value.Left, value => value.Right)
                .HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));
        }
    }

    /// <summary>Stores case-insensitive TreeIds while retaining exact CLR value snapshots.</summary>
    private sealed class AliasGuardTreeContext
        : GuardTreeContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public AliasGuardTreeContext(
            DbContextOptions<AliasGuardTreeContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override bool CaseInsensitive => true;

        /// <inheritdoc />
        protected override bool BroadComparer => false;
    }

    /// <summary>Stores case-sensitive TreeIds despite a broad domain and metadata value comparer.</summary>
    private sealed class BroadGuardTreeContext
        : GuardTreeContext
    {
        /// <summary>Uses the configured model and database connection.</summary>
        /// <param name="options">The exact Entity Framework options supplied by the fixture.</param>
        public BroadGuardTreeContext(
            DbContextOptions<BroadGuardTreeContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override bool CaseInsensitive => false;

        /// <inheritdoc />
        protected override bool BroadComparer => true;
    }
}
