namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores one ordered hierarchy with a mapped identity and matching nullable parent representation.</summary>
/// <typeparam name="TKey">The model identity type, including explicitly converted keys.</typeparam>
/// <typeparam name="TParent">The identity's nullable parent type.</typeparam>
public sealed class OrderingKeyNode<TKey, TParent> : IScopedNestedSetNode<TKey, Guid, int>
    where TKey : notnull
{
    /// <summary>Creates a node whose coordinates and scope will be assigned by the hierarchy service.</summary>
    /// <param name="id">The application-assigned identity.</param>
    /// <param name="name">The domain value used for sibling ordering.</param>
    public OrderingKeyNode(
        TKey id,
        string name
    )
    {
        Id = id;
        Name = name;
    }

    /// <summary>Gets or sets the nullable direct-parent identity.</summary>
    public TParent? ParentId { get; set; }

    /// <summary>Gets or sets the ascending domain criterion.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public TKey Id { get; set; }

    /// <summary>Gets or sets the independent hierarchy scope.</summary>
    public int Scope { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
