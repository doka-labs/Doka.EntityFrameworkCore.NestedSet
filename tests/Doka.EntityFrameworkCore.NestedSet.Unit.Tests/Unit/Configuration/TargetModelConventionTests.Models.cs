namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class TargetModelConventionTests
{
    /// <summary>Builds one isolated SQLite model without opening its connection.</summary>
    private sealed class TargetModelContext : DbContext, ITestModelVariant
    {
        /// <summary>The model configuration unique to this context instance.</summary>
        private readonly Action<EntityTypeBuilder<TargetNode>> _configure;

        /// <summary>Creates an independently cached test model.</summary>
        /// <param name="configure">The complete target entity configuration.</param>
        internal TargetModelContext(
            Action<EntityTypeBuilder<TargetNode>> configure
        ) : base(Options<TargetModelContext>())
        {
            _configure = configure;
        }

        /// <inheritdoc />
        object ITestModelVariant.ModelVariant { get; } = Guid.NewGuid();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => _configure(modelBuilder.Entity<TargetNode>());
    }

    /// <summary>Builds a valid hierarchy whose structure is stored entirely in shadow properties.</summary>
    private sealed class ShadowModelContext : DbContext
    {
        /// <summary>Creates a new isolated in-memory SQLite model.</summary>
        internal ShadowModelContext() : base(Options<ShadowModelContext>()) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<ShadowNode>();
            entity.HasKey(node => node.Id);
            entity.Property<Guid>("Tree");
            entity.Property<int?>("Parent");
            entity.Property<long>("LeftBoundary");
            entity.Property<long>("RightBoundary");
            entity.Property<int>("NodeDepth");
            entity.Property<long>("SiblingPosition");
            entity.HasNestedSet(builder => builder
                .HasNodeKey(node => node.Id)
                .HasTreeId("Tree")
                .HasParent("Parent")
                .HasBounds("LeftBoundary", "RightBoundary")
                .HasDepth("NodeDepth")
                .HasPosition("SiblingPosition"));
        }
    }

    /// <summary>Builds an invalid hierarchy to prove final validation sees string-selected property types.</summary>
    private sealed class InvalidShadowModelContext : DbContext
    {
        /// <summary>Creates a new isolated in-memory SQLite model.</summary>
        internal InvalidShadowModelContext() : base(Options<InvalidShadowModelContext>()) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<ShadowNode>();
            entity.HasKey(node => node.Id);
            entity.Property<Guid>("Tree");
            entity.Property<int?>("Parent");
            entity.Property<int>("LeftBoundary");
            entity.Property<long>("RightBoundary");
            entity.Property<int>("NodeDepth");
            entity.Property<long>("SiblingPosition");
            entity.HasNestedSet(builder => builder
                .HasNodeKey(node => node.Id)
                .HasTreeId("Tree")
                .HasParent("Parent")
                .HasBounds("LeftBoundary", "RightBoundary")
                .HasDepth("NodeDepth")
                .HasPosition("SiblingPosition"));
        }
    }

    /// <summary>Builds one isolated model with a selected invalid structural-role contract.</summary>
    private sealed class InvalidRoleModelContext : DbContext, ITestModelVariant
    {
        /// <summary>Creates a model for the requested invalid role variant.</summary>
        /// <param name="variant">The invalid role variant to configure.</param>
        internal InvalidRoleModelContext(
            string variant
        ) : base(Options<InvalidRoleModelContext>())
        {
            Variant = variant;
        }

        /// <summary>Gets the invalid model variant.</summary>
        private string Variant { get; }

        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => Variant;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<InvalidRoleNode>();
            entity.HasKey(node => node.Id);
            entity.Property(
                Variant == "tree-nullable"
                    ? typeof(Guid?)
                    : typeof(Guid),
                "Tree");

            entity.Property(
                Variant == "parent-string"
                    ? typeof(string)
                    : typeof(int?),
                "Parent");

            entity.Property(
                Variant == "left-nullable"
                    ? typeof(long?)
                    : typeof(long),
                "LeftBoundary");

            entity.Property(
                Variant switch
                {
                    "right-int" => typeof(int),
                    "right-nullable" => typeof(long?),
                    _ => typeof(long),
                },
                "RightBoundary");

            entity.Property(
                Variant switch
                {
                    "depth-long" => typeof(long),
                    "depth-nullable" => typeof(int?),
                    _ => typeof(int),
                },
                "NodeDepth");

            entity.Property(
                Variant switch
                {
                    "position-int" => typeof(int),
                    "position-nullable" => typeof(long?),
                    _ => typeof(long),
                },
                "SiblingPosition");

            entity.Property<int>("Scope");
            entity.HasNestedSet(builder => builder
                .HasNodeKey(node => node.Id)
                .HasTreeId("Tree")
                .HasScope("Scope")
                .HasParent("Parent")
                .HasBounds("LeftBoundary", "RightBoundary")
                .HasDepth("NodeDepth")
                .HasPosition("SiblingPosition"));

            var separator = Variant.IndexOf('-');

            if (separator < 0)
            {
                return;
            }

            var behavior = Variant[..separator];
            var role = Variant[(separator + 1)..] switch
            {
                "parent" => "Parent",
                "left" => "LeftBoundary",
                "right" => "RightBoundary",
                "depth" => "NodeDepth",
                "position" => "SiblingPosition",
                "scope" => "Scope",
                _ => null,
            };

            if (role is null)
            {
                return;
            }

            if (behavior == "generated")
            {
                entity
                    .Property(role)
                    .ValueGeneratedOnAdd();

                return;
            }

            if (behavior == "readonly")
            {
                entity
                    .Property(role)
                    .Metadata
                    .SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            }
        }
    }

    /// <summary>Supplies composite identity fields and the complete target hierarchy structure.</summary>
    private sealed class TargetNode
    {
        /// <summary>Gets or sets the first EF primary-key component.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the second EF primary-key component.</summary>
        public int RecordId { get; set; }

        /// <summary>Gets or sets the stable scalar hierarchy identity.</summary>
        public Guid NodeKey { get; set; }

        /// <summary>Gets or sets a non-hierarchy alternate identity used by one negative relationship test.</summary>
        public Guid OtherNodeKey { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets an application- or store-managed optimistic concurrency token.</summary>
        public long Revision { get; set; }

        /// <summary>Gets or sets the nullable direct parent identity.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>
        /// Gets or sets the nullable string parent identity used by the collation compatibility test.
        /// </summary>
        public string? ParentName { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the node depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the dense sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the first sibling-order value.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the second sibling-order value.</summary>
        public int Kind { get; set; }
    }

    /// <summary>Supplies the ordinary scalar key for invalid structural-role models.</summary>
    private sealed class InvalidRoleNode
    {
        /// <summary>Gets or sets the scalar node identity.</summary>
        public int Id { get; set; }
    }

    /// <summary>Supplies only an ordinary CLR key; all hierarchy roles are shadow properties.</summary>
    private sealed class ShadowNode
    {
        /// <summary>Gets or sets the scalar primary and node key.</summary>
        public int Id { get; set; }
    }
}
