namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Maps renamed shadow roles, including scalar NodeKey zero in a scoped composite primary key.</summary>
internal sealed class CompositionShadowContext : DbContext
{
    /// <summary>Names the shadow scope property.</summary>
    internal const string Scope = "HierarchyScope";

    /// <summary>Names the shadow scalar node key.</summary>
    internal const string Key = "HierarchyKey";

    /// <summary>Names the shadow tree identity.</summary>
    internal const string Tree = "HierarchyTree";

    /// <summary>Names the shadow nullable parent key.</summary>
    internal const string Parent = "HierarchyParent";

    /// <summary>Names the shadow inclusive left boundary.</summary>
    internal const string Start = "HierarchyStart";

    /// <summary>Names the shadow inclusive right boundary.</summary>
    internal const string End = "HierarchyEnd";

    /// <summary>Names the shadow stored structural depth.</summary>
    internal const string Depth = "HierarchyDepth";

    /// <summary>Names the shadow sibling position.</summary>
    internal const string Position = "HierarchyPosition";

    /// <summary>Creates the shadow-role context using the fixture's actual provider.</summary>
    internal CompositionShadowContext(
        DbContextOptions<CompositionShadowContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<CompositionShadowNode>();
        node.ToTable("CompositionShadowNodes");
        node.Ignore(entity => entity.Left);
        node.Ignore(entity => entity.Right);
        node.Ignore(entity => entity.Depth);
        node
            .Property(entity => entity.Label)
            .HasMaxLength(32);

        node.Property<Guid>(Scope);
        node
            .Property<int>(Key)
            .ValueGeneratedNever();

        node.Property<Guid>(Tree);
        node.Property<int?>(Parent);
        node.Property<long>(Start);
        node.Property<long>(End);
        node.Property<int>(Depth);
        node.Property<long>(Position);
        node.HasKey(Scope, Key);
        node.HasNestedSet(mapping => mapping
            .HasNodeKey(Key)
            .HasScope(Scope)
            .HasTreeId(Tree)
            .HasParent(Parent)
            .HasBounds(Start, End)
            .HasDepth(Depth)
            .HasPosition(Position));
    }
}
