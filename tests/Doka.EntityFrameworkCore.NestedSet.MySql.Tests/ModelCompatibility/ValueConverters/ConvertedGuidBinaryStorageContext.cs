namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Uses EF's Guid-to-byte-array conversion with explicit varbinary(16) columns.</summary>
internal sealed class ConvertedGuidBinaryStorageContext : GuidBinaryStorageContext
{
    /// <summary>Uses the fixture's exact provider and database.</summary>
    /// <param name="options">The options for the converted Guid model.</param>
    public ConvertedGuidBinaryStorageContext(
        DbContextOptions<ConvertedGuidBinaryStorageContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        base.OnModelCreating(modelBuilder);
        var node = modelBuilder.Entity<GuidBinaryStorageNode>();
        node
            .Property(entity => entity.Id)
            .HasConversion<byte[]>()
            .HasColumnType("varbinary(16)");

        node
            .Property(entity => entity.Scope)
            .HasConversion<byte[]>()
            .HasColumnType("varbinary(16)");

        node
            .Property(entity => entity.TreeId)
            .HasConversion<byte[]>()
            .HasColumnType("varbinary(16)");

        // WHY: EF lifts the same converter over nullable parents and handles database nulls separately.
        node
            .Property(entity => entity.ParentId)
            .HasConversion<byte[]>()
            .HasColumnType("varbinary(16)");
    }
}
