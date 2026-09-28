namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Uses converted NodeKeys with binary Scope identity to exercise native scalar membership probes.</summary>
internal sealed class ConvertedBroadScopeContext(DbContextOptions<ConvertedBroadScopeContext> options)
    : BroadScopeContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        var node = modelBuilder.Entity<BroadScopeNode>();
        node.ToTable("ConvertedBroadScopeNodes");
        ConfigureKeyConverter(node);
    }

    /// <summary>Preserves scalar storage while selecting the unsupported-key transport fallback.</summary>
    internal static void ConfigureKeyConverter(
        EntityTypeBuilder<BroadScopeNode> node
    )
    {
        // WHY: An explicit converter makes NodeKey transport ineligible for the verified native collection path,
        // while the persisted integer and Scope columns keep the same identity semantics as the base model.
        var converter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<int, int>(
            value => value,
            value => value);

        node
            .Property(value => value.Id)
            .HasConversion(converter);
        node
            .Property(value => value.ParentId)
            .HasConversion(converter);
    }
}
