namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Allows application comparers broader than native string equality without adding relationships.</summary>
public sealed class BulkStageGuardContext : DbContext
{
    /// <summary>Uses the fixture-owned provider configuration.</summary>
    public BulkStageGuardContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var collation = Database.IsSqlite()
            ? "BINARY"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_BIN2"
                    : "utf8mb4_bin";

        var text = modelBuilder.Entity<BulkStageTextNode>();
        var key = text
            .Property(node => node.Id)
            .HasMaxLength(64)
            .UseCollation(collation)
            .ValueGeneratedNever();

        // WHY: Providers may use case-insensitive string-key tracking independently of the binary SQL collation.
        // The fixture needs distinct A/a principals while only Parent and Scope deliberately use broader equality.
        key.Metadata.SetValueComparer(
            ValueComparer.CreateDefault<string>(
                favorStructuralComparisons: true));

        var scope = text
            .Property(node => node.Scope)
            .HasMaxLength(64)
            .UseCollation(collation);

        var parent = text
            .Property(node => node.ParentId)
            .HasMaxLength(64)
            .UseCollation(collation);

        var comparer = new ValueComparer<string?>(
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            value => value == null
                ? 0
                : StringComparer.OrdinalIgnoreCase.GetHashCode(value),
            value => value);

        // WHY: Only guard properties use broader CLR equality; the principal primary key stays ordinal.
        scope.Metadata.SetValueComparer(comparer);
        parent.Metadata.SetValueComparer(comparer);
        text.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId));

        var binaryParent = modelBuilder.Entity<BulkStageBinaryParent>();
        binaryParent
            .Property(node => node.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        binaryParent
            .Property(node => node.ParentId)
            .HasMaxLength(64);

        binaryParent.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId));

        var binaryScope = modelBuilder.Entity<BulkStageBinaryScope>();
        binaryScope
            .Property(node => node.Id)
            .ValueGeneratedNever();

        binaryScope
            .Property(node => node.Scope)
            .HasMaxLength(64)
            .Metadata
            .SetValueComparer(
                ValueComparer.CreateDefault<byte[]>(
                    favorStructuralComparisons: true));

        binaryScope.HasNestedSet(builder => builder
            .HasTreeId(node => node.TreeId)
            .HasScope(node => node.Scope)
            .HasParent(node => node.ParentId));
    }
}
