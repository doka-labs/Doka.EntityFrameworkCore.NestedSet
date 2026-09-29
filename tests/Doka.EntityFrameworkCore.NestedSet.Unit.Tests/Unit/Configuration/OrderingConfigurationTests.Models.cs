using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OrderingConfigurationTests
{
    /// <summary>Creates mutable metadata with mapped payload columns and optional existing ordering.</summary>
    private static EntityTypeBuilder<ConfigurationNode> CreateEntity(
        bool ordered
    )
    {
        var model = new ModelBuilder();
        var entity = model.Entity<ConfigurationNode>();
        entity.ToTable("ConfigurationNodes");
        entity.HasKey(node => node.Id);
        entity.Property(node => node.Name);
        entity.Property(node => node.OptionalCode);
        entity.Property(node => node.Rank);
        entity.HasNestedSet(builder =>
        {
            ConfigureStructure(builder);
            if (ordered)
            {
                builder.OrderBy(node => node.Name);
            }
        });

        return entity;
    }

    /// <summary>Selects every structural role independently from the domain ordering configuration.</summary>
    private static void ConfigureStructure(
        NestedSetBuilder<ConfigurationNode> builder
    ) => builder
        .HasBounds(node => node.Left, node => node.Right)
        .HasDepth(node => node.Depth)
        .HasPosition(node => node.Position)
        .HasScope(node => node.Scope)
        .HasTreeId(node => node.TreeId)
        .HasParent(node => node.ParentId);

    /// <summary>Produces each invalid selector without changing the arrangement of its theory case.</summary>
    private static void SelectInvalid(
        NestedSetBuilder<ConfigurationNode> builder,
        string variant
    )
    {
        switch (variant)
        {
            case "computed":
                builder.OrderBy(node => node.Name.ToUpperInvariant());
                break;
            case "nested":
                builder.OrderBy(node => node.Name.Length);
                break;
            case "boxed":
                builder.OrderBy(node => (object)node.Rank);
                break;
            case "null":
                builder.OrderBy<string>(null!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    /// <summary>Selects one structural property as the deliberately invalid domain criterion.</summary>
    private static void SelectStructural(
        NestedSetBuilder<ConfigurationNode> builder,
        string property
    )
    {
        switch (property)
        {
            case nameof(ConfigurationNode.Left):
                builder.OrderBy(node => node.Left);
                break;
            case nameof(ConfigurationNode.Right):
                builder.OrderBy(node => node.Right);
                break;
            case nameof(ConfigurationNode.Depth):
                builder.OrderBy(node => node.Depth);
                break;
            case nameof(ConfigurationNode.Position):
                builder.OrderBy(node => node.Position);
                break;
            case nameof(ConfigurationNode.ParentId):
                builder.OrderBy(node => node.ParentId);
                break;
            case nameof(ConfigurationNode.Scope):
                builder.OrderBy(node => node.Scope);
                break;
            case nameof(ConfigurationNode.TreeId):
                builder.OrderBy(node => node.TreeId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }

    /// <summary>Selects provider-limited CLR types through their direct mapped properties.</summary>
    private static void SelectUnsupportedSqlite(
        NestedSetBuilder<ConfigurationNode> builder,
        string property
    )
    {
        switch (property)
        {
            case nameof(ConfigurationNode.Price):
                builder.OrderBy(node => node.Price);
                break;
            case nameof(ConfigurationNode.Timestamp):
                builder.OrderBy(node => node.Timestamp);
                break;
            case nameof(ConfigurationNode.Duration):
                builder.OrderBy(node => node.Duration);
                break;
            case nameof(ConfigurationNode.Sequence):
                builder.OrderBy(node => node.Sequence);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }

    /// <summary>Captures annotation contents so array mutation cannot hide behind shared references.</summary>
    private static (string Name, string? Value)[] SnapshotAnnotations(
        EntityTypeBuilder<ConfigurationNode> entity
    ) => entity
        .Metadata
        .GetAnnotations()
        .OrderBy(annotation => annotation.Name, StringComparer.Ordinal)
        .Select(annotation => (annotation.Name, annotation.Value switch
        {
            string[] names => string.Join(",", names),
            bool[] descending => string.Join(",", descending),
            int[] nullSort => string.Join(",", nullSort),
            _ => Convert.ToString(annotation.Value, CultureInfo.InvariantCulture),
        }))
        .ToArray();

    /// <summary>Finalizes isolated SQLite models without opening a database connection.</summary>
    private sealed class ConfigurationContext : DbContext, ITestModelVariant
    {
        /// <summary>The optional order configuration applied after the structural roles are selected.</summary>
        private readonly Action<NestedSetBuilder<ConfigurationNode>>? _configureOrder;

        /// <summary>The optional payload mapping variation used by provider and generation contract tests.</summary>
        private readonly Action<EntityTypeBuilder<ConfigurationNode>>? _configureProperties;

        /// <summary>Creates a model-specific context; a shared identity deliberately reuses the same model.</summary>
        internal ConfigurationContext(
            Action<NestedSetBuilder<ConfigurationNode>>? configureOrder,
            Guid? identity = null,
            Action<EntityTypeBuilder<ConfigurationNode>>? configureProperties = null,
            IServiceProvider? services = null
        ) : base(Options(services))
        {
            _configureOrder = configureOrder;
            _configureProperties = configureProperties;
            Identity = identity ?? Guid.NewGuid();
        }

        /// <summary>Gets the test-owned model cache identity.</summary>
        internal Guid Identity { get; }

        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => Identity;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<ConfigurationNode>();

            entity.Ignore(node => node.Unmapped);
            entity
                .Property(node => node.Id)
                .ValueGeneratedNever();

            _configureProperties?.Invoke(entity);
            entity.HasNestedSet(builder =>
            {
                ConfigureStructure(builder);
                _configureOrder?.Invoke(builder);
            });
        }

        /// <summary>Installs SQLite conventions and a model cache keyed by the explicit test identity.</summary>
        private static DbContextOptions<ConfigurationContext> Options(IServiceProvider? services)
        {
            var options = new DbContextOptionsBuilder<ConfigurationContext>().ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets();

            if (services is null)
            {
                options.ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>();
            }
            else
            {
                options.UseInternalServiceProvider(services);
            }

            return options.Options;
        }
    }

    /// <summary>Separates hierarchy coordinates from payload fields used by ordering configuration.</summary>
    private sealed class ConfigurationNode
    {
        /// <summary>Gets or sets the single primary key used to break ordering ties.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the required hierarchy scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the stable tree identity within the scope.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the nullable parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left interval boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right interval boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the structural depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the structural sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the text payload used by the first domain ordering criterion.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets a numeric payload independent of the structural coordinates.</summary>
        public int Rank { get; set; }

        /// <summary>Gets or sets a nullable payload used to verify explicit null placement.</summary>
        public string? OptionalCode { get; set; }

        /// <summary>Gets or sets a decimal criterion requiring a supported SQLite provider representation.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets an offset-aware timestamp used by SQLite ordering rejection tests.</summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>Gets or sets a duration rejected as an unconverted SQLite ordering criterion.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Gets or sets an unsigned integer rejected as an unconverted SQLite ordering criterion.</summary>
        public ulong Sequence { get; set; }

        /// <summary>Gets or sets the deliberately ignored property used by invalid-mapping tests.</summary>
        public string Unmapped { get; set; } = "";
    }
}
