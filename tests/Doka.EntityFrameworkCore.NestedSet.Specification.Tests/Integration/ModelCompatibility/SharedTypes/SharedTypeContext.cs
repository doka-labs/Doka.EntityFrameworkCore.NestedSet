namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps two property-bag entity types so hierarchy access must retain the selected EF entity-type name.</summary>
internal sealed class SharedTypeContext : DbContext
{
    internal const string FolderEntity = "SharedFolder";
    internal const string UnrelatedEntity = "SharedUnrelated";
    internal const string Id = "Id";
    internal const string TreeId = "TreeId";
    internal const string ParentId = "ParentId";
    internal const string Left = "Left";
    internal const string Right = "Right";
    internal const string Depth = "Depth";
    internal const string Position = "Position";
    internal const string Name = "Name";

    /// <summary>Creates a shared-type compatibility context.</summary>
    internal SharedTypeContext(
        DbContextOptions<SharedTypeContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
            FolderEntity,
            entity =>
            {
                entity.ToTable("SharedFolders");
                entity
                    .IndexerProperty<int>(Id)
                    .ValueGeneratedNever();
                entity.IndexerProperty<Guid>(TreeId);
                entity.IndexerProperty<int?>(ParentId);
                entity.IndexerProperty<long>(Left);
                entity.IndexerProperty<long>(Right);
                entity.IndexerProperty<int>(Depth);
                entity.IndexerProperty<long>(Position);
                entity.IndexerProperty<string>(Name);
                entity.HasKey(Id);
                entity.HasNestedSet(nestedSet => nestedSet
                    .HasNodeKey(Id)
                    .HasTreeId(TreeId)
                    .HasParent(ParentId)
                    .HasBounds(Left, Right)
                    .HasDepth(Depth)
                    .HasPosition(Position));
            });

        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(
            UnrelatedEntity,
            entity =>
            {
                entity.ToTable("SharedUnrelated");
                entity
                    .IndexerProperty<int>(Id)
                    .ValueGeneratedNever();
                entity.HasKey(Id);
            });
    }
}
