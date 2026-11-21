namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Exercises an ordered hierarchy whose scalar coordinates have no original-value slots.</summary>
public sealed class OrderingNotificationNode : OrderingNotificationEntity, IScopedNestedSetNode<int, Guid, int>
{
    private int _depth;
    private int _id;
    private long _left;
    private string _name = "";
    private int? _parentId;
    private long _position;
    private long _right;
    private int _scope;
    private Guid _treeId;

    /// <summary>Gets or sets the optional direct parent.</summary>
    public int? ParentId
    {
        get => _parentId;
        set => SetValue(ref _parentId, value, nameof(ParentId));
    }

    /// <summary>Gets or sets the alphabetic sibling ordering value.</summary>
    public string Name
    {
        get => _name;
        set => SetValue(ref _name, value, nameof(Name));
    }

    /// <summary>Gets or sets the stable tree identity.</summary>
    public Guid TreeId
    {
        get => _treeId;
        set => SetValue(ref _treeId, value, nameof(TreeId));
    }

    /// <inheritdoc />
    public int Id
    {
        get => _id;
        set => SetValue(ref _id, value, nameof(Id));
    }

    /// <summary>Gets or sets the isolated forest scope.</summary>
    public int Scope
    {
        get => _scope;
        set => SetValue(ref _scope, value, nameof(Scope));
    }

    /// <inheritdoc />
    public long Left
    {
        get => _left;
        set => SetValue(ref _left, value, nameof(Left));
    }

    /// <inheritdoc />
    public long Right
    {
        get => _right;
        set => SetValue(ref _right, value, nameof(Right));
    }

    /// <inheritdoc />
    public int Depth
    {
        get => _depth;
        set => SetValue(ref _depth, value, nameof(Depth));
    }

    /// <inheritdoc />
    public long Position
    {
        get => _position;
        set => SetValue(ref _position, value, nameof(Position));
    }
}
