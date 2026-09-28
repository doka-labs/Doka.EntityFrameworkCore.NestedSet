namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Represents an old supplied model with property-only capture and no physical hierarchy ownership.</summary>
internal sealed class StaleCollationCaptureContext(DbContextOptions<StaleCollationCaptureContext> options)
    : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<TextNode>();
        node
            .Property(row => row.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();
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

        // WHY: Old generated models kept only property-wide values. That representation cannot express
        // different physical collations for concrete TPC tables, even when its global capture flag is present.
        foreach (var name in new[] { nameof(TextNode.Id), nameof(TextNode.ParentId), nameof(TextNode.Tree) })
        {
            node
                .Property(name)
                .HasAnnotation(NestedSetAnnotationNames.Collation, string.Empty);
        }

        modelBuilder.HasAnnotation(NestedSetAnnotationNames.CollationsCaptured, true);
    }
}
