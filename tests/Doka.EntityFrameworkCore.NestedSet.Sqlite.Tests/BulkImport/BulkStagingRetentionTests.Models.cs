using Doka.NestedSet;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Doka.EntityFrameworkCore.NestedSet.Tests.Sqlite;

public sealed partial class BulkStagingRetentionTests
{
    /// <summary>Maps a shadow scope to exercise ownership before the first graph callback.</summary>
    private sealed class ShadowStagingContext(DbContextOptions<ShadowStagingContext> options)
        : NestedSetDbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            var node = builder.Entity<StagingNode>();
            node.ToTable("ShadowStagingRetentionNodes");
            node.Ignore(value => value.Scope);
            node.Ignore(value => value.Payload);
            node.Ignore(value => value.FailGetter);
            node.Ignore(value => value.FailSetter);
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node
                .Property(value => value.Version)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate();
            node
                .Property<int>("ShadowScope")
                .Metadata
                .SetValueComparer(
                    new ValueComparer<int>(
                        (left, right) => SameShadowScope(left, right),
                        value => value,
                        value => value));

            node.HasKey("ShadowScope", nameof(StagingNode.Id));
            node.HasNestedSet(set => set
                .HasNodeKey(value => value.Id)
                .HasTreeId(value => value.TreeId)
                .HasScope("ShadowScope")
                .HasParent(value => value.ParentId));
            builder
                .Entity<StagingMarker>()
                .ToTable("ShadowStagingRetentionMarkers");
        }
    }

    /// <summary>Maps accessor modes explicitly so the regression exercises application CLR callbacks.</summary>
    private sealed class StagingContext : NestedSetDbContext
    {
        /// <summary>Uses the isolated SQLite database configuration.</summary>
        internal StagingContext(
            DbContextOptions<StagingContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<StagingNode>();
            node.ToTable("StagingRetentionNodes");
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();
            node.HasAlternateKey(value => value.Alias);
            node
                .Property(value => value.Left)
                .UsePropertyAccessMode(PropertyAccessMode.Property);
            node
                .Property(value => value.Version)
                .HasDefaultValue(0)
                .ValueGeneratedOnAddOrUpdate()
                .UsePropertyAccessMode(PropertyAccessMode.Property);

            node.Ignore(value => value.FailGetter);
            node.Ignore(value => value.FailSetter);
            node.HasNestedSet(builder => builder
                .HasTreeId(value => value.TreeId)
                .HasScope(value => value.Scope)
                .HasParent(value => value.ParentId));
            node.OwnsOne(
                value => value.Payload,
                payload => payload
                    .HasOne(value => value.Foreign)
                    .WithMany());

            modelBuilder
                .Entity<StagingMarker>()
                .ToTable("StagingRetentionMarkers");
            modelBuilder
                .Entity<StagingForeign>()
                .ToTable("StagingRetentionForeigns");
        }
    }

    /// <summary>Provides one-shot failures that permit exact rollback after their first invocation.</summary>
    private sealed class StagingNode : IScopedNestedSetNode<int, Guid, int>
    {
        private long _left;
        private int _version;

        /// <inheritdoc />
        public int Id { get; set; }

        /// <summary>Gets or sets the secondary identity used to exercise partially installed key maps.</summary>
        public string Alias { get; set; } = "input";

        /// <inheritdoc />
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the configured scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the nullable parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets an optional owned payload whose lifecycle belongs to the input aggregate.</summary>
        public StagingPayload? Payload { get; set; }

        /// <summary>Gets or sets whether the next generated getter throws.</summary>
        public bool FailGetter { get; set; }

        /// <summary>Gets or sets whether the next left-bound assignment throws.</summary>
        public bool FailSetter { get; set; }

        /// <inheritdoc />
        public long Left
        {
            get => _left;
            set
            {
                if (FailSetter)
                {
                    FailSetter = false;

                    throw new InjectedCommandException();
                }

                _left = value;
            }
        }

        /// <inheritdoc />
        public long Right { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public long Position { get; set; }

        /// <summary>Gets or sets the generated value whose initial capture can fail.</summary>
        public int Version
        {
            get
            {
                if (FailGetter)
                {
                    FailGetter = false;

                    throw new InjectedCommandException();
                }

                return _version;
            }
            set => _version = value;
        }
    }

    /// <summary>Provides a separately tracked owned child in the partial insertion graph.</summary>
    private sealed class StagingPayload
    {
        /// <summary>Gets or sets ordinary caller-owned payload.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Gets or sets a non-owned navigation whose populated graph must be rejected.</summary>
        public StagingForeign? Foreign { get; set; }
    }

    /// <summary>Provides foreign application data outside the explicit hierarchy aggregate.</summary>
    private sealed class StagingForeign
    {
        /// <summary>Gets or sets its conventional application identity.</summary>
        public int Id { get; set; }
    }

    /// <summary>Keeps unrelated context-owned tracking alive while the failed input is collected.</summary>
    private sealed class StagingMarker
    {
        /// <summary>Gets or sets the application-owned identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets application payload persisted after an import failure.</summary>
        public string Name { get; set; } = string.Empty;
    }
}
