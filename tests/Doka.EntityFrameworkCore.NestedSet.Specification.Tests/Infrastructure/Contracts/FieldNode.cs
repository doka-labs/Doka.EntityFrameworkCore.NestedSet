namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exposes decorated structural getters to reveal incorrect reflection-based reads.</summary>
public sealed class FieldNode : IScopedNestedSetNode<int, Guid, int>
{
    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId { get; set; }

    private int _depth;
    private long _left;
    private long _right;

    /// <summary>Gets or sets the isolated hierarchy scope.</summary>
    public int Tree { get; set; }

    int IScopedNestedSetNode<int, Guid, int>.Scope => Tree;

    /// <summary>Gets or sets the parent key, or null for a root.</summary>
    public int? ParentId { get; set; }

    /// <inheritdoc />
    public int Id { get; set; }

    /// <summary>Gets a decorated left boundary or sets the raw value stored by EF.</summary>
    /// <remarks>The offset reveals readers that bypass the configured field-access mapping.</remarks>
    public long Left
    {
        get => _left + 1000;
        set => _left = value;
    }

    /// <summary>Gets a decorated right boundary or sets the raw value stored by EF.</summary>
    /// <remarks>The offset reveals readers that bypass the configured field-access mapping.</remarks>
    public long Right
    {
        get => _right + 1000;
        set => _right = value;
    }

    /// <summary>Gets a decorated depth or sets the raw value stored by EF.</summary>
    /// <remarks>The offset reveals readers that bypass the configured field-access mapping.</remarks>
    public int Depth
    {
        get => _depth + 1000;
        set => _depth = value;
    }

    /// <inheritdoc />
    public long Position { get; set; }
}
