namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Stores an ordered hierarchy independent of another closed hierarchy type.</summary>
/// <typeparam name="TScope">The scope-key type shared with an application-owned anchor.</typeparam>
/// <typeparam name="THierarchy">The marker separating independent EF entity types.</typeparam>
internal sealed class OrderingLockNode<TScope, THierarchy> : IScopedNestedSetNode<int, Guid, TScope>
    where TScope : notnull
{
    /// <summary>Gets or sets the optional direct parent.</summary>
    public int? ParentId { get; set; }

    /// <summary>Gets or sets the domain value determining sibling order.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets or sets the persisted scope value, which may be a database-equal alias.</summary>
    public required TScope Scope { get; set; }

    /// <inheritdoc />
    public long Left { get; set; }

    /// <inheritdoc />
    public long Right { get; set; }

    /// <inheritdoc />
    public int Depth { get; set; }

    /// <inheritdoc />
    public long Position { get; set; }
}
