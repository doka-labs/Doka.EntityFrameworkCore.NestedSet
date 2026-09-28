namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies physical maintenance-key backing across supported key-configuration orders.</summary>
public sealed class EnterpriseIndexMappingTests
{
    /// <summary>Verifies that supported mappings provide a unique key over scope and node identity.</summary>
    /// <param name="variant">The key-discovery order to exercise.</param>
    [Theory]
    [InlineData("conventional")]
    [InlineData("configured")]
    [InlineData("explicit-before-ef-key")]
    [InlineData("automatic-after-ef-key")]
    public void MaintenanceKeyCoversScopeAndNodeIdentity(
        string variant
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<IndexMappingContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .Options;

        using var context = new IndexMappingContext(options, variant);

        // Act
        var model = context.Model;

        // Assert
        var entity = model.FindEntityType(variant == "conventional" ? typeof(GuidNode) : typeof(TreeNode))!;
        var nodeKey = entity.FindProperty(variant == "conventional" ? "Id" : "NodeId")!;
        var key = Assert.Single(
            entity.GetKeys(),
            candidate => candidate.Properties is [{ Name: "Tree" }, _] && candidate.Properties[1] == nodeKey);

        Assert.NotNull(key.GetName());
    }
}

/// <summary>Builds one key-configuration order without sharing its model with other variants.</summary>
public sealed class IndexMappingContext : DbContext, ITestModelVariant
{
    /// <summary>Creates an isolated model used to verify maintenance-index configuration.</summary>
    /// <param name="options">The SQLite options and variant-aware model cache.</param>
    /// <param name="variant">The key-configuration order.</param>
    public IndexMappingContext(
        DbContextOptions<IndexMappingContext> options,
        string variant
    ) : base(options)
    {
        Variant = variant;
    }

    /// <summary>Gets the model variant included in the cache key.</summary>
    public string Variant { get; }

    /// <inheritdoc />
    object ITestModelVariant.ModelVariant => Variant;

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        if (Variant == "conventional")
        {
            modelBuilder
                .Entity<GuidNode>()
                .HasNestedSet(builder => builder
                    .HasTreeId(node => node.TreeId)
                    .HasScope(node => node.Tree)
                    .HasParent(node => node.ParentId));

            return;
        }

        var entity = modelBuilder.Entity<TreeNode>();

        if (Variant == "configured")
        {
            entity.HasKey(node => node.NodeId);
        }

        entity.HasNestedSet(builder =>
        {
            builder
                .HasBounds(node => node.Start, node => node.End)
                .HasDepth(node => node.Depth)
                .HasTreeId(node => node.TreeId)
                .HasScope(node => node.Tree)
                .HasParent(node => node.Parent)
                .HasPosition(node => node.Position);

            if (Variant == "explicit-before-ef-key")
            {
                builder.HasNodeKey(node => node.NodeId);
            }
        });
        entity.HasKey(node => node.NodeId);
    }
}
