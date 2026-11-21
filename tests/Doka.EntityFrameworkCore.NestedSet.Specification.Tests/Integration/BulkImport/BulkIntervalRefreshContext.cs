namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps update-generated values that must be selected after unresolved parents are assigned.</summary>
public sealed class BulkIntervalRefreshContext : DbContext
{
    /// <summary>Creates an independent context using the fixture's provider and command observers.</summary>
    public BulkIntervalRefreshContext(
        DbContextOptions<BulkIntervalRefreshContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<BulkIntervalNode>();
        node.ToTable(nameof(BulkIntervalNode));
        node
            .Property(value => value.Name)
            .HasMaxLength(80)
            .HasDefaultValue("Generated");

        var sql = this.GetService<ISqlGenerationHelper>();
        var sum = $"COALESCE({sql.DelimitIdentifier(nameof(BulkIntervalNode.ParentId))}, 0)"
            + $" + {sql.DelimitIdentifier(nameof(BulkIntervalNode.Depth))}"
            + $" + {sql.DelimitIdentifier(nameof(BulkIntervalNode.Position))}";

        var expression = Database.IsSqlServer()
            ? $"CAST({sum} AS int)"
            : sum;

        // WHY: Computed values change after parent repair on every provider, independently of trigger timing.
        node
            .Property(value => value.Version)
            .HasComputedColumnSql(expression, stored: true)
            .IsConcurrencyToken();

        var details = node.ComplexProperty(value => value.Details);
        details
            .Property(value => value.Label)
            .HasMaxLength(80)
            .HasDefaultValue("Generated detail");

        details
            .Property(value => value.Revision)
            .HasComputedColumnSql(expression + " + 1000", stored: true);

        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));

        var collation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_BIN2"
                    : "utf8mb4_bin";

        var text = modelBuilder.Entity<BulkStageTextNode>();
        text.ToTable(nameof(BulkStageTextNode));
        var key = text
            .Property(value => value.Id)
            .HasMaxLength(64)
            .UseCollation(collation)
            .ValueGeneratedNever();

        // WHY: SQL distinguishes C/c while application equality deliberately does not. Refresh must verify the
        // exact inserted key instead of allowing a same-count replacement through the application's comparer.
        key.Metadata.SetValueComparer(
            new ValueComparer<string>(
                (left,
                    right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                value => StringComparer.OrdinalIgnoreCase.GetHashCode(value),
                value => value));

        text
            .Property(value => value.Scope)
            .HasMaxLength(64)
            .UseCollation(collation);

        text
            .Property(value => value.ParentId)
            .HasMaxLength(64)
            .UseCollation(collation);

        text.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));

        var binary = modelBuilder.Entity<BulkStageBinaryParent>();
        binary.ToTable(nameof(BulkStageBinaryParent));
        binary
            .Property(value => value.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        binary
            .Property(value => value.ParentId)
            .HasMaxLength(64);

        binary.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId));
    }
}
