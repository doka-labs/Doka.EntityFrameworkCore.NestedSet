namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

/// <summary>Represents payload whose entire hierarchy structure is stored in EF shadow properties.</summary>
internal sealed class ShadowNode
{
    /// <summary>Gets or sets the application-assigned row and NodeKey identity.</summary>
    internal int Id { get; set; }

    /// <summary>Gets or sets application payload.</summary>
    internal string Name { get; set; } = "";
}

/// <summary>Maps every structural role except NodeKey without CLR members.</summary>
internal sealed class ShadowPropertyContext : DbContext
{
    internal const string TreeId = "TreeId";
    internal const string ParentId = "ParentId";
    internal const string Left = "Left";
    internal const string Right = "Right";
    internal const string Depth = "Depth";
    internal const string Position = "Position";

    /// <summary>Creates a shadow-property compatibility context.</summary>
    internal ShadowPropertyContext(
        DbContextOptions<ShadowPropertyContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<ShadowNode>();
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();
        node.Property<Guid>(TreeId);
        node.Property<int?>(ParentId);
        node.Property<long>(Left);
        node.Property<long>(Right);
        node.Property<int>(Depth);
        node.Property<long>(Position);
        node.HasNestedSet(nestedSet => nestedSet
            .HasNodeKey(entity => entity.Id)
            .HasTreeId(TreeId)
            .HasParent(ParentId)
            .HasBounds(Left, Right)
            .HasDepth(Depth)
            .HasPosition(Position));
    }
}
