namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies typed tree-registry model derivation independently of runtime locking.</summary>
public sealed class TreeRegistryConventionTests
{
    /// <summary>Copies converted string identity facets and uses Scope plus TreeId as the registry key.</summary>
    [Fact]
    public void ScopedRegistryCopiesIdentityStoreSemantics()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RegistryContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        using var context = new RegistryContext(options);

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;
        var node = model.FindEntityType(typeof(ScopedNode))!;
        var registry = RegistryFor(node, model);

        // Assert
        Assert.Equal(["Scope", "TreeId"], registry.FindPrimaryKey()!.Properties.Select(property => property.Name));
        AssertFacet(node.FindProperty(nameof(ScopedNode.Scope))!, registry.FindProperty("Scope")!);
        AssertFacet(node.FindProperty(nameof(ScopedNode.TreeId))!, registry.FindProperty("TreeId")!);
        Assert.Equal(typeof(long), registry.FindProperty("Revision")!.ClrType);
        Assert.Equal(typeof(byte), registry.FindProperty("Lifecycle")!.ClrType);
    }

    /// <summary>Omits Scope entirely when a hierarchy is configured without an application partition.</summary>
    [Fact]
    public void UnscopedRegistryUsesTreeIdAsItsOnlyIdentity()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RegistryContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        using var context = new RegistryContext(options);

        // Act
        var model = context.GetService<IDesignTimeModel>().Model;
        var node = model.FindEntityType(typeof(UnscopedNode))!;
        var registry = RegistryFor(node, model);

        // Assert
        Assert.Null(registry.FindProperty("Scope"));
        Assert.Equal(["TreeId"], registry.FindPrimaryKey()!.Properties.Select(property => property.Name));
        AssertFacet(node.FindProperty(nameof(UnscopedNode.TreeId))!, registry.FindProperty("TreeId")!);
    }

    /// <summary>Compares every facet that changes parameter transport or database identity semantics.</summary>
    private static void AssertFacet(
        IProperty source,
        IProperty registry
    )
    {
        Assert.Equal(source.ClrType, registry.ClrType);
        Assert.Equal(source.GetColumnType(), registry.GetColumnType());
        Assert.Equal(source.GetMaxLength(), registry.GetMaxLength());
        Assert.Equal(source.IsUnicode(), registry.IsUnicode());
        Assert.Equal(source.GetCollation(), registry.GetCollation());
        Assert.Equal(
            source
                .GetRelationalTypeMapping()
                .Converter
                ?.GetType(),
            registry
                .GetRelationalTypeMapping()
                .Converter
                ?.GetType());
    }

    /// <summary>Finds the exact named shared registry referenced by one hierarchy.</summary>
    private static IEntityType RegistryFor(
        IEntityType hierarchy,
        IModel model
    )
    {
        var name = Assert.IsType<string>(hierarchy.FindAnnotation("Doka:NestedSet:TreeRegistryEntity")?.Value);

        return Assert.IsType<IEntityType>(model.FindEntityType(name), exactMatch: false);
    }

    /// <summary>Maps one scoped and one unscoped hierarchy through the normal options registration.</summary>
    private sealed class RegistryContext : DbContext
    {
        /// <summary>Creates the test context.</summary>
        public RegistryContext(
            DbContextOptions<RegistryContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var scoped = modelBuilder.Entity<ScopedNode>();
            scoped.HasKey(node => node.Id);
            scoped
                .Property(node => node.Scope)
                .HasMaxLength(80)
                .IsUnicode(false)
                .UseCollation("NOCASE");
            scoped
                .Property(node => node.TreeId)
                .HasConversion<string>()
                .HasMaxLength(36)
                .IsUnicode(false)
                .UseCollation("NOCASE");
            scoped.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(node => node.Id)
                .HasScope(node => node.Scope)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));

            var unscoped = modelBuilder.Entity<UnscopedNode>();
            unscoped.HasKey(node => node.Id);
            unscoped
                .Property(node => node.TreeId)
                .HasMaxLength(120)
                .IsUnicode(false)
                .UseCollation("NOCASE");
            unscoped.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(node => node.Id)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
        }
    }

    /// <summary>Provides converted scoped identity properties for convention verification.</summary>
    private sealed class ScopedNode
    {
        /// <summary>Gets or sets the node key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the application partition.</summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }

    /// <summary>Provides an unscoped string TreeId for convention verification.</summary>
    private sealed class UnscopedNode
    {
        /// <summary>Gets or sets the node key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public string TreeId { get; set; } = string.Empty;

        /// <summary>Gets or sets the direct parent key.</summary>
        public int? ParentId { get; set; }

        /// <summary>Gets or sets the left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the sibling position.</summary>
        public long Position { get; set; }
    }
}
