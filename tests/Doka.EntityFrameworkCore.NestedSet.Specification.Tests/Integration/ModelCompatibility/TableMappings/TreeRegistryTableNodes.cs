namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Provides one hierarchy node shape for the named-registry contexts.</summary>
internal abstract class RegistryTableNode
{
    /// <summary>Gets or sets the stable identity.</summary>
    internal long Id { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    internal Guid TreeId { get; set; }

    /// <summary>Gets or sets the direct parent identity.</summary>
    internal long? ParentId { get; set; }

    /// <summary>Gets or sets the inclusive left boundary.</summary>
    internal long Left { get; set; }

    /// <summary>Gets or sets the inclusive right boundary.</summary>
    internal long Right { get; set; }

    /// <summary>Gets or sets the distance from the root.</summary>
    internal int Depth { get; set; }

    /// <summary>Gets or sets the sibling position.</summary>
    internal long Position { get; set; }

    /// <summary>Gets or sets application payload.</summary>
    internal string Name { get; set; } = string.Empty;

    /// <summary>Maps one concrete node with an optional explicit registry table.</summary>
    internal static void Map<TNode>(
        ModelBuilder modelBuilder,
        string table,
        string? registryTable
    )
        where TNode : RegistryTableNode
    {
        var node = modelBuilder.Entity<TNode>();
        node.ToTable(table);
        node
            .Property(entity => entity.Id)
            .ValueGeneratedNever();

        node.HasNestedSet(nestedSet =>
        {
            nestedSet
                .HasNodeKey(entity => entity.Id)
                .HasTreeId(entity => entity.TreeId)
                .HasParent(entity => entity.ParentId)
                .HasBounds(entity => entity.Left, entity => entity.Right)
                .HasDepth(entity => entity.Depth)
                .HasPosition(entity => entity.Position);

            if (registryTable is not null)
            {
                nestedSet.HasTreeRegistryTable(registryTable);
            }
        });
    }
}
