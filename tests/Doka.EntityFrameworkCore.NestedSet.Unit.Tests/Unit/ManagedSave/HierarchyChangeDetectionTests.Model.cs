using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Creates a stable synthetic hierarchy node without opening a database connection.</summary>
    internal static ProbeNode CreateNode(
        int id = 1
    ) => new()
    {
        Id = id,
        Scope = 7,
        TreeId = Guid.Parse("a1f7cde9-bd9f-4b39-b6a3-e0b23dac9264"),
        Left = 1,
        Right = 2,
        Name = "Alpha",
    };

    /// <summary>Changes one role through its CLR member while automatic detection remains disabled.</summary>
    private static void ChangeRole(
        ProbeNode node,
        string propertyName
    )
    {
        switch (propertyName)
        {
            case nameof(ProbeNode.Id):       node.Id++; break;
            case nameof(ProbeNode.Scope):    node.Scope++; break;
            case nameof(ProbeNode.TreeId):   node.TreeId = Guid.NewGuid(); break;
            case nameof(ProbeNode.ParentId): node.ParentId = 42; break;
            case nameof(ProbeNode.Left):     node.Left++; break;
            case nameof(ProbeNode.Right):    node.Right++; break;
            case nameof(ProbeNode.Depth):    node.Depth++; break;
            case nameof(ProbeNode.Position): node.Position++; break;
            case nameof(ProbeNode.Name):     node.Name = "Bravo"; break;
            default:                         throw new ArgumentOutOfRangeException(nameof(propertyName));
        }
    }

    /// <summary>Fails immediately if a typed equality optimization bypasses EF's null guards.</summary>
    private static bool CompareNonNullText(
        string? left,
        string? right
    )
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(left, right, StringComparison.Ordinal);
    }

    /// <summary>Provides ordinary snapshot tracking with caller-controlled change detection.</summary>
    internal sealed class SnapshotContext : ProbeContext
    {
        /// <summary>Uses an unopened SQLite connection for ordinary read-only tracker probes.</summary>
        internal SnapshotContext() { }

        /// <summary>Accepts the externally configured options required by EF's real context pool.</summary>
        public SnapshotContext(
            DbContextOptions<SnapshotContext> options
        ) : base(options) { }
    }

    /// <summary>Omits collection relationship snapshots while preserving notification-based FK fixup.</summary>
    private sealed class NotificationContext : ProbeContext
    {
        /// <inheritdoc />
        protected override ChangeTrackingStrategy TrackingStrategy
            => ChangeTrackingStrategy.ChangingAndChangedNotifications;
    }

    /// <summary>Uses the real SQLite provider services exclusively for in-memory tracking probes.</summary>
    internal abstract class ProbeContext : DbContext
    {
        /// <summary>Disables automatic detection before any tracked test state is created.</summary>
        protected ProbeContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <summary>Preserves externally configured pool options while disabling automatic detection.</summary>
        protected ProbeContext(
            DbContextOptions options
        ) : base(options)
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <summary>Gets the entity-specific strategy whose relationship snapshot contract is exercised.</summary>
        protected virtual ChangeTrackingStrategy TrackingStrategy => ChangeTrackingStrategy.Snapshot;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        )
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());
            }
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<ProbeNode>();
            node.HasChangeTrackingStrategy(TrackingStrategy);
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node
                .Property(value => value.Name)
                .Metadata
                .SetValueComparer(
                    new ValueComparer<string>(
                        (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                        value => StringComparer.OrdinalIgnoreCase.GetHashCode(value),
                        value => value));

            node.HasNestedSet(builder => builder
                .HasScope(value => value.Scope)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .OrderBy(value => value.Name));

            node
                .HasOne(value => value.Parent)
                .WithMany(value => value.Children)
                .HasPrincipalKey(value => new
                {
                    value.Scope,
                    value.Id,
                })
                .HasForeignKey(value => new
                {
                    value.Scope,
                    value.ParentId,
                })
                .OnDelete(DeleteBehavior.Restrict);

            node
                .HasOne(value => value.Owner)
                .WithMany()
                .HasForeignKey(value => value.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            node
                .HasOne(value => value.ScopeOwner)
                .WithMany(value => value.ScopeNodes)
                .HasForeignKey(value => value.Scope)
                .OnDelete(DeleteBehavior.Restrict);

            var owner = modelBuilder.Entity<ProbeOwner>();
            owner
                .Property(value => value.Id)
                .ValueGeneratedNever();

            owner.Property<int>("ShadowOrder");
            owner
                .Property(value => value.StrictText)
                .Metadata
                .SetValueComparer(
                    new ValueComparer<string?>(
                        (left, right) => CompareNonNullText(left, right),
                        value => value == null
                            ? 0
                            : StringComparer.Ordinal.GetHashCode(value),
                        value => value));
        }
    }

    /// <summary>Models an unrelated tracked principal whose payload must remain outside hierarchy detection.</summary>
    internal sealed class ProbeOwner
    {
        /// <summary>Gets or sets the fixed application-owned primary key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets a scalar unrelated to all hierarchy roles.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets or sets an optional scalar whose custom equality rejects null arguments.</summary>
        public string? StrictText { get; set; }

        /// <summary>Gets the nodes whose configured hierarchy scope references this application principal.</summary>
        public ICollection<ProbeNode> ScopeNodes { get; } = new List<ProbeNode>();
    }

    /// <summary>Supports both snapshot and notification strategies with the same hierarchy role mapping.</summary>
    internal sealed class ProbeNode
        : IScopedNestedSetNode<int, Guid, int>, INotifyPropertyChanging, INotifyPropertyChanged
    {
        private ObservableCollection<ProbeNode>? _children = [];
        private int _depth;
        private int _id;
        private long _left;
        private string _name = string.Empty;
        private ProbeOwner? _owner;
        private int? _ownerId;
        private ProbeNode? _parent;
        private int? _parentId;
        private string _payload = string.Empty;
        private long _position;
        private long _right;
        private int _scope;
        private ProbeOwner? _scopeOwner;
        private Guid _treeId;

        /// <summary>Gets or sets the optional direct parent's node key.</summary>
        public int? ParentId { get => _parentId; set => Set(ref _parentId, value, nameof(ParentId)); }

        /// <summary>Gets or sets the configured domain ordering criterion.</summary>
        public string Name { get => _name; set => Set(ref _name, value, nameof(Name)); }

        /// <summary>Gets or sets payload excluded from the hierarchy-role probe.</summary>
        public string Payload { get => _payload; set => Set(ref _payload, value, nameof(Payload)); }

        /// <summary>Gets or sets an unrelated principal key excluded from structural roles.</summary>
        public int? OwnerId { get => _ownerId; set => Set(ref _ownerId, value, nameof(OwnerId)); }

        /// <summary>Gets or sets the unrelated tracked principal.</summary>
        public ProbeOwner? Owner { get => _owner; set => Set(ref _owner, value, nameof(Owner)); }

        /// <summary>Gets or sets the principal whose key occupies the hierarchy's scope role.</summary>
        public ProbeOwner? ScopeOwner { get => _scopeOwner; set => Set(ref _scopeOwner, value, nameof(ScopeOwner)); }

        /// <summary>Gets or sets the parent reference whose CLR edits precede snapshot fixup.</summary>
        public ProbeNode? Parent { get => _parent; set => Set(ref _parent, value, nameof(Parent)); }

        /// <summary>Gets or sets the direct children with notification support when the strategy requires it.</summary>
        public ObservableCollection<ProbeNode>? Children
        {
            get => _children;
            set => Set(ref _children, value, nameof(Children));
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <inheritdoc />
        public event PropertyChangingEventHandler? PropertyChanging;

        /// <inheritdoc />
        public int Id { get => _id; set => Set(ref _id, value, nameof(Id)); }

        /// <inheritdoc />
        public int Scope { get => _scope; set => Set(ref _scope, value, nameof(Scope)); }

        /// <inheritdoc />
        public Guid TreeId { get => _treeId; set => Set(ref _treeId, value, nameof(TreeId)); }

        /// <inheritdoc />
        public long Left { get => _left; set => Set(ref _left, value, nameof(Left)); }

        /// <inheritdoc />
        public long Right { get => _right; set => Set(ref _right, value, nameof(Right)); }

        /// <inheritdoc />
        public int Depth { get => _depth; set => Set(ref _depth, value, nameof(Depth)); }

        /// <inheritdoc />
        public long Position { get => _position; set => Set(ref _position, value, nameof(Position)); }

        /// <summary>Raises both EF notification stages around an actual CLR value transition.</summary>
        private void Set<TValue>(
            ref TValue field,
            TValue value,
            string propertyName
        )
        {
            if (EqualityComparer<TValue>.Default.Equals(field, value))
            {
                return;
            }

            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Maps a concrete TPC subtype while retaining its base-declared application relationship.</summary>
    private sealed class InheritedScopeContext : DbContext
    {
        /// <summary>Keeps all test edits unmarked until the hierarchy probe inspects them.</summary>
        internal InheritedScopeContext()
        {
            ChangeTracker.AutoDetectChangesEnabled = false;
        }

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var root = modelBuilder.Entity<InheritedScopeBase>();
            root.UseTpcMappingStrategy();
            root.HasKey(value => value.Id);
            root
                .Property(value => value.Id)
                .ValueGeneratedNever();

            root
                .HasOne(value => value.ScopeOwner)
                .WithMany(value => value.Nodes)
                .HasForeignKey(value => value.Scope)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder
                .Entity<InheritedScopeOwner>()
                .Property(value => value.Id)
                .ValueGeneratedNever();

            var node = modelBuilder.Entity<InheritedScopeNode>();
            node.ToTable("InheritedScopeNodes");
            node.HasNestedSet(builder => builder
                .HasNodeKey(value => value.Id)
                .HasScope(value => value.Scope)
                .HasTreeId(value => value.TreeId)
                .HasParent(value => value.ParentId)
                .HasBounds(value => value.Left, value => value.Right)
                .HasDepth(value => value.Depth)
                .HasPosition(value => value.Position));

            modelBuilder
                .Entity<OrdinaryScopeNode>()
                .ToTable("OrdinaryScopeNodes");
        }
    }

    /// <summary>Declares the scope FK outside the concrete hierarchy's own EF metadata.</summary>
    private abstract class InheritedScopeBase
    {
        /// <summary>Gets or sets the inherited application key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the inherited FK reused as the concrete hierarchy's scope role.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the inherited principal reference.</summary>
        public InheritedScopeOwner? ScopeOwner { get; set; }
    }

    /// <summary>Owns the base-declared collection without being a configured hierarchy entity itself.</summary>
    private sealed class InheritedScopeOwner
    {
        /// <summary>Gets or sets the scope principal's fixed key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets unrelated application payload that must remain undetected.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets the inherited dependents whose FK contributes to concrete hierarchy scope.</summary>
        public List<InheritedScopeBase> Nodes { get; } = [];
    }

    /// <summary>Represents a supported concrete TPC hierarchy with inherited key and scope.</summary>
    private sealed class InheritedScopeNode : InheritedScopeBase
    {
        /// <summary>Gets or sets the containing tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the optional direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the number of ancestors.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the zero-based sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Shares the inherited application relationship without any nested-set configuration.</summary>
    private sealed class OrdinaryScopeNode : InheritedScopeBase;
}
