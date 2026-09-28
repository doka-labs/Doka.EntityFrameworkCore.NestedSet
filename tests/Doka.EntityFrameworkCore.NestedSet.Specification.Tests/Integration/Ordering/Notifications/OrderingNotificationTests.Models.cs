namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Supplies real change notifications without retaining originals for ordinary scalar properties.</summary>
public abstract class OrderingNotificationEntity : System.ComponentModel.INotifyPropertyChanging,
    System.ComponentModel.INotifyPropertyChanged
{
    /// <inheritdoc />
    public event System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;

    /// <inheritdoc />
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises the paired events required by EF's changing-and-changed notification strategy.</summary>
    /// <typeparam name="TValue">The mapped CLR property type.</typeparam>
    /// <param name="field">The backing storage changed by the domain setter.</param>
    /// <param name="value">The new domain value.</param>
    /// <param name="name">The mapped property name reported to EF.</param>
    protected void SetValue<TValue>(
        ref TValue field,
        TValue value,
        string name
    )
    {
        if (EqualityComparer<TValue>.Default.Equals(field, value))
        {
            return;
        }

        PropertyChanging?.Invoke(this, new System.ComponentModel.PropertyChangingEventArgs(name));
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}

/// <summary>Exercises an ordered hierarchy whose scalar coordinates have no original-value slots.</summary>
public sealed class OrderingNotificationNode : OrderingNotificationEntity, IScopedNestedSetNode<int, Guid, int>
{
    private Guid _treeId;
    private int _id;
    private int _scope;
    private int? _parentId;
    private string _name = "";
    private long _left;
    private long _right;
    private int _depth;
    private long _position;

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

/// <summary>Represents pending notification-tracked application data unrelated to hierarchy coordinates.</summary>
public sealed class OrderingNotificationPayload : OrderingNotificationEntity
{
    private int _id;
    private string _value = "";

    /// <summary>Gets or sets the application-assigned identity.</summary>
    public int Id
    {
        get => _id;
        set => SetValue(ref _id, value, nameof(Id));
    }

    /// <summary>Gets or sets an ordinary scalar for which EF does not retain an original value.</summary>
    public string Value
    {
        get => _value;
        set => SetValue(ref _value, value, nameof(Value));
    }
}

/// <summary>Uses notification tracking and independent tables in an existing relational test database.</summary>
public sealed class OrderingNotificationContext : NestedSetDbContext
{
    /// <summary>Creates the context against the database owned by the test.</summary>
    /// <param name="options">The provider connection and optional fault injection.</param>
    public OrderingNotificationContext(
        DbContextOptions options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        var node = modelBuilder.Entity<OrderingNotificationNode>();
        node.ToTable("NotificationOrderingNodes");
        node.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotifications);
        node
            .Property(value => value.Id)
            .ValueGeneratedNever();
        node.HasNestedSet(builder => builder
            .HasTreeId(value => value.TreeId)
            .HasScope(value => value.Scope)
            .HasParent(value => value.ParentId)
            .OrderBy(value => value.Name));

        var payload = modelBuilder.Entity<OrderingNotificationPayload>();
        payload.ToTable("NotificationOrderingPayloads");
        payload.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotifications);
        payload
            .Property(value => value.Id)
            .ValueGeneratedNever();
    }
}
