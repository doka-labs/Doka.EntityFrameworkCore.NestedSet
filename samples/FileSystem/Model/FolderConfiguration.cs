namespace Doka.EntityFrameworkCore.NestedSet.Samples.FileSystem;

/// <summary>Configures independent folder trees, strict sibling ordering, and their parent relationship.</summary>
public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    /// <inheritdoc />
    public void Configure(
        EntityTypeBuilder<Folder> builder
    )
    {
        builder.HasKey(folder => folder.Id);

        builder
            .Property(folder => folder.Name)
            .HasMaxLength(120);

        builder
            .Property(folder => folder.Category)
            .HasMaxLength(40);

        // WHY: Scope is optional. TreeId alone isolates every independently numbered tree in this model.
        // The interface supplies Id, TreeId, bounds, depth, and position without repeating their selectors.
        // NestedSet appends the node key as a stable tie-breaker and derives indexes from this configuration.
        builder.HasNestedSet(node => node
            .HasParent(folder => folder.ParentId)
            .OrderBy(folder => folder.Name)
            .ThenBy(folder => folder.Category));

        // WHY: DeleteAsync promotes children explicitly; a database cascade would delete them instead.
        builder
            .HasOne<Folder>()
            .WithMany()
            .HasForeignKey(folder => folder.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
