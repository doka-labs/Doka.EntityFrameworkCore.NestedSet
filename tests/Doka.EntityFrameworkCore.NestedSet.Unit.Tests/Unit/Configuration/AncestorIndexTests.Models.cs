using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>Maps explicit hierarchy roles independently of CLR convenience interfaces.</summary>
    private static EntityTypeBuilder<AncestorNode> Configure(
        ModelBuilder model,
        bool scoped = true
    )
    {
        var entity = model.Entity<AncestorNode>();
        entity.HasKey(node => node.Id);
        entity.HasNestedSet(builder =>
        {
            builder.HasTreeId(node => node.TreeId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasParent(node => node.ParentId)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position);

            if (scoped)
            {
                builder.HasScope(node => node.Scope);
            }
        });

        return entity;
    }

    /// <summary>Maps a named dictionary hierarchy without initializing any database-generated implementation value.</summary>
    private static EntityTypeBuilder<Dictionary<string, object>> ConfigureBag(
        ModelBuilder model
    )
    {
        var entity = model.SharedTypeEntity<Dictionary<string, object>>("NamedHierarchy");
        entity.Property<int>("NodeId");
        entity.Property<Guid>("TreeIdentity");
        entity.Property<int?>("Parent");
        entity.Property<long>("L");
        entity.Property<long>("R");
        entity.Property<int>("D");
        entity.Property<long>("P");
        entity.HasKey("NodeId");
        entity.HasNestedSet(builder => builder
            .HasNodeKey("NodeId")
            .HasTreeId("TreeIdentity")
            .HasParent("Parent")
            .HasBounds("L", "R")
            .HasDepth("D")
            .HasPosition("P"));

        return entity;
    }

    /// <summary>Supplies one real-provider convention graph per independently cached model variant.</summary>
    private sealed class AncestorContext : DbContext, ITestModelVariant
    {
        /// <summary>The model configuration executed after all provider conventions are installed.</summary>
        private readonly Action<ModelBuilder> _configure;
        private readonly OwnedModelTestServices<AncestorContext> _services;

        /// <summary>Creates one disconnected model-building context for the requested engine.</summary>
        internal AncestorContext(
            string engine,
            Action<ModelBuilder> configure
        ) : this(
            OwnedModelTestServices<AncestorContext>.Create(Options(engine), static services =>
                services.Replace(ServiceDescriptor.Singleton<IModelCacheKeyFactory, TestModelCacheKeyFactory>())),
            configure)
        { }

        /// <summary>Binds the complete owned provider before EF initializes the context.</summary>
        private AncestorContext(
            OwnedModelTestServices<AncestorContext> services,
            Action<ModelBuilder> configure
        ) : base(services.Options)
        {
            _services = services;
            _configure = configure;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            try
            {
                base.Dispose();
            }
            finally
            {
                _services.Dispose();
            }
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            try
            {
                await base.DisposeAsync();
            }
            finally
            {
                await _services.DisposeAsync();
            }
        }

        /// <inheritdoc />
        object ITestModelVariant.ModelVariant { get; } = Guid.NewGuid();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => _configure(modelBuilder);

        /// <summary>Uses real provider metadata services while leaving every database connection unopened.</summary>
        private static DbContextOptions<AncestorContext> Options(
            string engine
        )
        {
            var options = new DbContextOptionsBuilder<AncestorContext>()
                .ConfigureTestWarnings();

            switch (engine)
            {
                case "Sqlite":
                    options.UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());
                    break;
                case "MySql":
                    options.UseMySql(
                        "Server=127.0.0.1;Database=Ancestor;User ID=unused",
                        DatabaseTestTargets.MySql,
                        provider => provider.UseNestedSets());
                    break;
                case "MariaDb":
                    options.UseMySql(
                        "Server=127.0.0.1;Database=Ancestor;User ID=unused",
                        DatabaseTestTargets.MariaDb,
                        provider => provider.UseNestedSets());
                    break;
                case "PostgreSql":
                    options.UseNpgsql(
                        "Host=127.0.0.1;Database=Ancestor;Username=unused",
                        provider => provider.UseNestedSets());
                    break;
                case "SqlServer":
                    options.UseSqlServer(
                        "Server=127.0.0.1;Database=Ancestor;User ID=unused;Password=unused",
                        provider => provider.UseNestedSets());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(engine));
            }

            return options.Options;
        }
    }

    /// <summary>Supplies independent CLR structure, scope, and optional application-owned Boolean metadata.</summary>
    private class AncestorNode
    {
        /// <summary>Gets or sets the stable scalar node identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the optional application partition.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left coordinate.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right coordinate.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the node depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets an optional application CLR property used to verify adoption.</summary>
        public bool NonLeaf { get; set; }
    }

    /// <summary>Supplies an independently materialized subtype for inherited generated-property mapping.</summary>
    private sealed class DerivedAncestorNode : AncestorNode
    {
        /// <summary>Gets or sets subtype payload outside the structural table under TPT.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Provides an application relationship that must not adopt the generated flag as a foreign key.</summary>
    private sealed class BooleanPrincipal
    {
        /// <summary>Gets or sets the deliberately Boolean principal identity.</summary>
        public bool Id { get; set; }
    }

    /// <summary>Supplies required application payload sharing the hierarchy table through an identifying relationship.</summary>
    private sealed class AncestorPayload
    {
        /// <summary>Gets or sets the shared row identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets independently materialized application payload.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Supplies an abstract TPC mapping root without an additional writable base table.</summary>
    private abstract class TpcAncestorNode : AncestorNode;

    /// <summary>Supplies one independently configured concrete TPC table.</summary>
    private sealed class ConcreteTpcAncestorNode : TpcAncestorNode;

    /// <summary>Supplies an unconfigured sibling table that must not inherit an opted-in generated column.</summary>
    private sealed class OtherTpcAncestorNode : TpcAncestorNode;
}
