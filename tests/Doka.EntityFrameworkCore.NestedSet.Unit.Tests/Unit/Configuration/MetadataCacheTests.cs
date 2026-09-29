using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Verifies that immutable mapping reuse follows model identity rather than context or entity type alone.
/// </summary>
public sealed class MetadataCacheTests
{
    /// <summary>Contexts sharing the same finalized model reuse the same validated mapping.</summary>
    [Fact]
    public async Task ContextsSharingModelReuseMapping()
    {
        // Arrange
        await using var services = CreateServices();
        await using var first = new CacheContext(services, 0);
        await using var second = new CacheContext(services, 0);
        var original = NestedSetMapping<CacheNode, int, int>.For(first, first.Model.FindEntityType(typeof(CacheNode))!);

        // Act
        var reused = NestedSetMapping<CacheNode, int, int>.For(second, second.Model.FindEntityType(typeof(CacheNode))!);

        // Assert
        Assert.Same(first.Model, second.Model);
        Assert.Same(original, reused);
        Assert.Equal(nameof(CacheNode.FirstLeft), reused.Left);
        Assert.Same(original.PropertyMapping(original.Left), reused.PropertyMapping(reused.Left));
    }

    /// <summary>Different models of the same CLR entity cannot reuse incompatible selectors or role names.</summary>
    [Fact]
    public async Task DifferentModelsHaveIndependentMappings()
    {
        // Arrange
        await using var services = CreateServices();
        await using var first = new CacheContext(services, 0);
        await using var second = new CacheContext(services, 1);
        var original = NestedSetMapping<CacheNode, int, int>.For(first, first.Model.FindEntityType(typeof(CacheNode))!);

        // Act
        var changed = NestedSetMapping<CacheNode, int, int>.For(
            second,
            second.Model.FindEntityType(typeof(CacheNode))!);

        // Assert
        Assert.NotSame(first.Model, second.Model);
        Assert.NotSame(original, changed);
        Assert.Equal(nameof(CacheNode.FirstLeft), original.Left);
        Assert.Equal(nameof(CacheNode.SecondLeft), changed.Left);
        Assert.NotSame(original.Projection, changed.Projection);
        Assert.NotSame(original.PropertyMapping(original.Left), changed.PropertyMapping(changed.Left));
        Assert.Equal("left one", original.PropertyMapping(original.Left).ColumnName);
        Assert.Equal("left two", changed.PropertyMapping(changed.Left).ColumnName);
    }

    /// <summary>Preserves finalized EF property and parameter metadata when physical column names differ.</summary>
    [Fact]
    public async Task PropertyDescriptorsRetainPhysicalMapping()
    {
        // Arrange
        await using var services = CreateServices();
        await using var context = new CacheContext(services, 0);
        var mapping = NestedSetMapping<CacheNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(CacheNode))!);

        // Act
        var descriptor = mapping.PropertyMapping(mapping.Left);

        // Assert
        Assert.Same(mapping.LeftProperty, descriptor.Property);
        Assert.Same(mapping.EntityType.FindProperty(nameof(CacheNode.FirstLeft)), descriptor.Property);
        Assert.Equal("left one", descriptor.ColumnName);
        Assert.Same(mapping.LeftProperty.GetRelationalTypeMapping(), descriptor.TypeMapping);
        Assert.Equal("INTEGER", descriptor.TypeMapping.StoreType);
        Assert.Equal("Hierarchy cache", mapping.Store.Name);
        Assert.Null(mapping.Store.Schema);
        Assert.Same(mapping.KeyProperty, mapping.PropertyMapping(mapping.Key).Property);
        Assert.Same(mapping.ScopeProperty, mapping.PropertyMapping(mapping.Scope).Property);
        Assert.Same(mapping.ParentProperty, mapping.PropertyMapping(mapping.Parent).Property);
        Assert.Same(mapping.RightProperty, mapping.PropertyMapping(mapping.Right).Property);
        Assert.Same(mapping.DepthProperty, mapping.PropertyMapping(mapping.Depth).Property);
        Assert.Same(mapping.PositionProperty, mapping.PropertyMapping(mapping.Position).Property);
    }

    /// <summary>Allows unordered models to select the native save path without inspecting tracked entries.</summary>
    [Fact]
    public async Task UnorderedModelHasNoSaveOrdering()
    {
        // Arrange
        await using var services = CreateServices();
        await using var context = new CacheContext(services, 0);
        var entity = context.Model.FindEntityType(typeof(CacheNode))!;

        // Act
        var metadata = NestedSetModelMapping.For(context.Model);

        // Assert
        Assert.False(metadata.HasOrdering);
        Assert.Null(metadata.Ordering(entity));
    }

    /// <summary>Shares total order and structural metadata between save scans and hierarchy operations.</summary>
    [Fact]
    public async Task OrderedModelReusesOperationMetadata()
    {
        // Arrange
        await using var services = CreateServices();
        await using var context = new CacheContext(services, 2);
        var mapping = NestedSetMapping<CacheNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(CacheNode))!);

        // Act
        var metadata = NestedSetModelMapping.For(context.Model);

        // Assert
        Assert.True(metadata.HasOrdering);
        Assert.Same(mapping.Order, metadata.Ordering(mapping.EntityType));
        Assert.Equal(
            [nameof(CacheNode.Name), nameof(CacheNode.Id)],
            mapping.Order!.Properties.Select(item => item.Name));
        Assert.Contains(mapping.KeyProperty, metadata.StructuralProperties(mapping.EntityType));
        Assert.Contains(mapping.ParentProperty, metadata.StructuralProperties(mapping.EntityType));
        Assert.Equal(8, metadata.StructuralProperties(mapping.EntityType).Count);
    }

    /// <summary>Copies mutable EF annotation arrays before publishing the shared model descriptor.</summary>
    [Fact]
    public async Task DescriptorDoesNotExposeOrderingAnnotationArray()
    {
        // Arrange
        await using var services = CreateServices();
        await using var context = new CacheContext(services, 2);
        var entity = context.Model.FindEntityType(typeof(CacheNode))!;
        var metadata = NestedSetModelMapping.For(context.Model);
        var descriptor = metadata.Descriptor(entity);
        var annotationDirections = (bool[])entity.FindAnnotation("Doka:NestedSet:OrderDescending")!.Value!;

        // Act
        annotationDirections[0] = true;

        // Assert
        Assert.False(descriptor.Order[0].Descending);
    }

    /// <summary>Owns the deliberately replaced model-cache service for exactly one model-lifetime experiment.</summary>
    private static ServiceProvider CreateServices()
    {
        // WHY: This test measures reuse inside one specific EF service graph, not process-global provider caching.
        // Explicit ownership avoids retaining a test-only CacheKeyFactory graph among the real-provider fixtures.

        var services = new ServiceCollection()
            .AddEntityFrameworkSqlite()
            .AddSingleton<IModelCacheKeyFactory, CacheKeyFactory>();

        // WHY: UseInternalServiceProvider transfers ownership of the complete EF service graph to the caller.
        // Registering the same internal services as UseNestedSets keeps this cache-isolation test representative.
        ServiceCollectionDescriptorExtensions.TryAddEnumerable(
            services,
            ServiceDescriptor
                .Scoped<IConventionSetPlugin,
                    NestedSetConventionSetPlugin>());
        ServiceCollectionDescriptorExtensions.TryAddEnumerable(
            services,
            ServiceDescriptor
                .Singleton<IInterceptor,
                    Features.ManagedSave.NestedSetSaveGuardInterceptor>());

        return services.BuildServiceProvider();
    }

    /// <summary>Separates the model variants used to verify the library's weak model cache.</summary>
    public sealed class CacheKeyFactory : IModelCacheKeyFactory
    {
        /// <inheritdoc />
        public object Create(
            DbContext context,
            bool designTime
        ) => (context.GetType(), ((CacheContext)context).Variant, designTime);
    }

    /// <summary>Maps one of two structural layouts onto the same CLR entity type.</summary>
    private sealed class CacheContext : DbContext
    {
        /// <summary>Creates a context without opening a database connection.</summary>
        /// <param name="services">The test-owned graph shared by both contexts.</param>
        /// <param name="variant">The structural column pair selected by the model.</param>
        internal CacheContext(
            IServiceProvider services,
            int variant
        ) : base(
            new DbContextOptionsBuilder<CacheContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .UseInternalServiceProvider(services)
                .Options)
        {
            Variant = variant;
        }

        /// <summary>Gets the model variant incorporated into EF's model cache key.</summary>
        internal int Variant { get; }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<CacheNode>()
                .ToTable("Hierarchy cache");
            modelBuilder
                .Entity<CacheNode>()
                .Property(node => node.FirstLeft)
                .HasColumnName("left one");
            modelBuilder
                .Entity<CacheNode>()
                .Property(node => node.SecondLeft)
                .HasColumnName("left two");
            modelBuilder
                .Entity<CacheNode>()
                .HasNestedSet(configuration =>
                {
                    configuration
                        .HasScope(node => node.Scope)
                        .HasTreeId(node => node.TreeId)
                        .HasParent(node => node.ParentId)
                        .HasDepth(node => node.Depth)
                        .HasPosition(node => node.Position);

                    // WHY: Model identity must isolate role mappings even when the entity and generic arguments match.
                    if (Variant == 0)
                    {
                        configuration.HasBounds(node => node.FirstLeft, node => node.FirstRight);
                    }
                    else
                    {
                        configuration.HasBounds(node => node.SecondLeft, node => node.SecondRight);
                    }

                    if (Variant == 2)
                    {
                        configuration.OrderBy(node => node.Name);
                    }
                });
        }
    }

    /// <summary>Supplies two alternative coordinate pairs to the model-isolation cases.</summary>
    private sealed class CacheNode
    {
        /// <summary>Gets or sets the primary key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the payload value used by the ordered model variant.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the forest scope.</summary>
        public int Scope { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the nullable parent identity.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the first layout's left coordinate.</summary>
        public long FirstLeft { get; set; }

        /// <summary>Gets or sets the first layout's right coordinate.</summary>
        public long FirstRight { get; set; }

        /// <summary>Gets or sets the second layout's left coordinate.</summary>
        public long SecondLeft { get; set; }

        /// <summary>Gets or sets the second layout's right coordinate.</summary>
        public long SecondRight { get; set; }

        /// <summary>Gets or sets the persisted depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the zero-based sibling position.</summary>
        public long Position { get; set; }
    }
}
