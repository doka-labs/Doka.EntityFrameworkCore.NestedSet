using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Doka.EntityFrameworkCore.NestedSet.Features.Queries;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class CompositionCoordinateProjectionTests
{
    /// <summary>Creates metadata-only SQLite options with the actual nested-set model conventions.</summary>
    private static DbContextOptionsBuilder<CoordinateContext> CoordinateOptions()
        => new DbContextOptionsBuilder<CoordinateContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

    /// <summary>Owns the model cache so the weak-lifetime test does not exercise EF's global provider cache.</summary>
    private static ServiceProvider CoordinateServices(DbContextOptionsBuilder<CoordinateContext> options)
        => CoordinateServices(options.Options);

    /// <summary>Isolates provider services for both structural selector and direct projection test contexts.</summary>
    private static ServiceProvider CoordinateServices(DbContextOptions options)
    {
        // WHY: Independent test models must not accumulate global EF singleton providers and make
        // an unrelated later sample fail with ManyServiceProvidersCreatedWarning during a full run.
        var services = new ServiceCollection();

        foreach (var extension in options.Extensions)
        {
            extension.ApplyServices(services);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Invokes the private structural seam without activating a provider capability or running I/O.</summary>
    private static (IQueryable Query, LambdaExpression Selector) Coordinates(
        CoordinateContext context,
        string mapping,
        bool includeNonLeaf
    )
    {
        var entity = context.Model.FindEntityType(mapping)!;
        var root = new NestedSetQuery<Dictionary<string, object>>(context, entity);
        var nodes = context.Set<Dictionary<string, object>>(mapping).AsQueryable();

        return CoordinateSelection(root, nodes, includeNonLeaf);
    }

    /// <summary>Extracts the exact immutable selector passed to the deferred Select call.</summary>
    private static (IQueryable Query, LambdaExpression Selector) CoordinateSelection<TEntity>(
        NestedSetQuery<TEntity> root,
        IQueryable<TEntity> nodes,
        bool includeNonLeaf
    )
        where TEntity : class
    {
        var method = root.GetType().GetMethod("AncestorIndexCoordinates",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(root, [nodes, includeNonLeaf]));
        var select = Assert.IsAssignableFrom<MethodCallExpression>(query.Expression);
        var quote = Assert.IsType<UnaryExpression>(select.Arguments[1]);

        return (query, Assert.IsAssignableFrom<LambdaExpression>(quote.Operand));
    }

    /// <summary>Returns weak witnesses after owning roots, contexts, and provider services have left scope.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] BuildCollectibleCoordinates()
    {
        var options = CoordinateOptions();
        using var provider = CoordinateServices(options);
        using var context = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        _ = Coordinates(context, CoordinateContext.Scoped, includeNonLeaf: true);

        return [new WeakReference(context), new WeakReference(context.Model),
            new WeakReference(context.Model.FindEntityType(CoordinateContext.Scoped)!)];
    }

    /// <summary>Optionally exposes the actual model as a deliberate strong-root control.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] BuildCollectibleCoordinates(
        out IModel model
    )
    {
        // WHY: The retained control uses a separate frame so the original collectible factory stays
        // unchanged. The counterfactual must isolate witness observation rather than factory lifetime.
        var options = CoordinateOptions();
        using var provider = CoordinateServices(options);
        using var context = new CoordinateContext(options.UseInternalServiceProvider(provider).Options);
        _ = Coordinates(context, CoordinateContext.Scoped, includeNonLeaf: true);
        model = context.Model;

        return [new WeakReference(context), new WeakReference(model),
            new WeakReference(model.FindEntityType(CoordinateContext.Scoped)!)];
    }

    /// <summary>Applies the unchanged collection budget without observing a witness target in the GC frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectCoordinateGarbage()
    {
        // WHY: Reading weak targets can introduce strong JIT temporaries. Keep every witness observation
        // outside this noninlined collection frame so the probe cannot extend the lifetime it measures.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    /// <summary>Maps the same CLR property bag with independent role names and one shadow-role entity.</summary>
    private sealed class CoordinateContext(DbContextOptions<CoordinateContext> options) : DbContext(options)
    {
        /// <summary>Names a scope-partitioned mapping with renamed interval properties.</summary>
        internal const string Scoped = "ScopedCoordinates";

        /// <summary>Names an unscoped mapping with a different role layout.</summary>
        internal const string Unscoped = "UnscopedCoordinates";

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureBag(modelBuilder, Scoped, scoped: true);
            ConfigureBag(modelBuilder, Unscoped, scoped: false);

            var node = modelBuilder.Entity<CoordinateNode>();
            node.HasKey(entity => entity.Id);
            node.Property(entity => entity.Id).ValueGeneratedNever();
            node.Property<Guid>("TreeId").HasConversion<string>();
            node.Property<int?>("ParentId");
            node.Property<long>("Left");
            node.Property<long>("Right");
            node.Property<int>("Depth");
            node.Property<long>("Position");
            node.HasMany(entity => entity.Items).WithOne().HasForeignKey(entity => entity.NodeId);
            node.HasNestedSet(hierarchy => hierarchy
                .HasNodeKey(entity => entity.Id)
                .HasTreeId("TreeId")
                .HasParent("ParentId")
                .HasBounds("Left", "Right")
                .HasDepth("Depth")
                .HasPosition("Position")
                .HasAncestorIndex());
        }

        /// <summary>Configures explicit property-bag roles without depending on the library's default names.</summary>
        private static void ConfigureBag(
            ModelBuilder modelBuilder,
            string mapping,
            bool scoped
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(mapping, entity =>
            {
                var key = scoped ? "NodeId" : "Key";
                var tree = scoped ? "TreeId" : "Tree";
                var parent = scoped ? "ParentId" : "Parent";
                var left = scoped ? "Start" : "Left";
                var right = scoped ? "End" : "Right";
                entity.ToTable(mapping);
                entity.Property<int>(key).ValueGeneratedNever();
                entity.Property<Guid>(tree).HasConversion<string>();
                entity.Property<int?>(parent);
                entity.Property<long>(left);
                entity.Property<long>(right);
                entity.Property<int>("Depth");
                entity.Property<long>("Position");

                if (scoped)
                {
                    entity.Property<int>("TenantId");
                    entity.HasKey("TenantId", key);
                }
                else
                {
                    entity.HasKey(key);
                }

                entity.HasNestedSet(hierarchy =>
                {
                    hierarchy.HasNodeKey(key).HasTreeId(tree).HasParent(parent).HasBounds(left, right)
                        .HasDepth("Depth").HasPosition("Position")
                        .HasAncestorIndex(scoped ? "HasChildren" : "Branches");

                    if (scoped)
                    {
                        hierarchy.HasScope("TenantId");
                    }
                });
            });
        }
    }

    /// <summary>Leaves structural roles in shadow storage while retaining an ordinary collection.</summary>
    private sealed class CoordinateNode
    {
        /// <summary>Gets or sets the visible application identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets the application-owned collection used by deferred composition.</summary>
        public List<CoordinateItem> Items { get; } = [];
    }

    /// <summary>Provides an application payload that cannot be moved into structural coordinate projection.</summary>
    private sealed class CoordinateItem
    {
        /// <summary>Gets or sets the payload identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets its mapped hierarchy owner.</summary>
        public int NodeId { get; set; }
    }
}
