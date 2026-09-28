namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Models an older application that configured infrastructure without registering finalization conventions.
/// </summary>
public sealed class LegacyCompiledContext : DbContext
{
    /// <summary>Creates an isolated legacy model or reuses that model explicitly.</summary>
    public LegacyCompiledContext(
        DbContextOptions<LegacyCompiledContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TextNode>();
        var collation = Database.IsSqlite()
            ? "NOCASE"
            : Database.IsNpgsql()
                ? "C"
                : Database.IsSqlServer()
                    ? "Latin1_General_100_CI_AS"
                    : "utf8mb4_unicode_ci";

        node
            .Property(row => row.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever()
            .UseCollation(collation);
        node
            .Property(row => row.ParentId)
            .HasMaxLength(64);
        node
            .Property(row => row.Tree)
            .HasMaxLength(64);
        node.HasNestedSet(builder => builder
            .HasTreeId(row => row.TreeId)
            .HasScope(row => row.Tree)
            .HasParent(row => row.ParentId));
    }
}
