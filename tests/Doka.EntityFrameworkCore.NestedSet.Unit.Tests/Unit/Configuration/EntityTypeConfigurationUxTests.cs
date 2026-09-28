namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the primary options and IEntityTypeConfiguration nested-set setup.</summary>
public sealed class EntityTypeConfigurationUxTests
{
    /// <summary>Applies a scoped ordered hierarchy through IEntityTypeConfiguration only.</summary>
    [Fact]
    public void ScopedConfigurationProducesCompleteHierarchyMetadata()
    {
        // Arrange
        using var context = new ScopedContext();

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(ScopedNode))!;
        var indexes = entity
            .GetIndexes()
            .Select(index => new
            {
                Properties = index
                    .Properties
                    .Select(property => property.Name)
                    .ToArray(),
                Descending = index.IsDescending?.ToArray() ?? new bool[index.Properties.Count],
            })
            .ToArray();

        var parent = entity
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(ScopedNode.ParentId)));

        var checks = entity
            .GetCheckConstraints()
            .Select(constraint => constraint.Sql)
            .OrderBy(expression => expression, StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(
            [nameof(ScopedNode.TenantId), nameof(ScopedNode.RecordId)],
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(
            entity.GetKeys(),
            key => key
                .Properties
                .Select(property => property.Name)
                .SequenceEqual([nameof(ScopedNode.TenantId), nameof(ScopedNode.NodeKey)]));
        Assert.Equal(nameof(ScopedNode.NodeKey), entity.FindAnnotation("Doka:NestedSet:NodeKey")?.Value);
        Assert.Equal(nameof(ScopedNode.TreeId), entity.FindAnnotation("Doka:NestedSet:TreeId")?.Value);
        Assert.Equal(nameof(ScopedNode.TenantId), entity.FindAnnotation("Doka:NestedSet:Scope")?.Value);
        Assert.Equal(5, indexes.Length);
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
                [nameof(ScopedNode.TenantId), nameof(ScopedNode.TreeId), nameof(ScopedNode.Left)]));
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
                [nameof(ScopedNode.TenantId), nameof(ScopedNode.TreeId), nameof(ScopedNode.Right)]));
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
            [
                nameof(ScopedNode.TenantId),
                nameof(ScopedNode.TreeId),
                nameof(ScopedNode.ParentId),
                nameof(ScopedNode.Position),
            ]));

        var order = Assert.Single(
            indexes,
            index => index.Properties.SequenceEqual(
            [
                nameof(ScopedNode.TenantId),
                nameof(ScopedNode.TreeId),
                nameof(ScopedNode.ParentId),
                nameof(ScopedNode.Name),
                nameof(ScopedNode.Kind),
                nameof(ScopedNode.NodeKey),
            ]));

        Assert.Equal(
            [
                false,
                false,
                false,
                false,
                true,
                false,
            ],
            order.Descending);
        Assert.Equal(
            [nameof(ScopedNode.TenantId), nameof(ScopedNode.ParentId)],
            parent.Properties.Select(property => property.Name));
        Assert.Equal(
            [nameof(ScopedNode.TenantId), nameof(ScopedNode.NodeKey)],
            parent.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        Assert.DoesNotContain(parent.Properties, property => property.Name == nameof(ScopedNode.TreeId));
        Assert.Equal(["\"Depth\" >= 0", "\"Left\" >= 1", "\"Position\" >= 0", "\"Right\" > \"Left\""], checks);
    }

    /// <summary>Omits Scope without requiring context-level nested-set configuration hooks.</summary>
    [Fact]
    public void ScopelessConfigurationProducesTreeLocalMetadata()
    {
        // Arrange
        using var context = new ScopelessContext();

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(ScopelessNode))!;

        var indexes = entity
            .GetIndexes()
            .Select(index => new
            {
                Properties = index
                    .Properties
                    .Select(property => property.Name)
                    .ToArray(),
                Descending = index.IsDescending?.ToArray() ?? new bool[index.Properties.Count],
            })
            .ToArray();

        var parent = entity
            .GetForeignKeys()
            .Single(foreignKey =>
                foreignKey.Properties.Any(property => property.Name == nameof(ScopelessNode.ParentId)));

        // Assert
        Assert.Equal(
            [nameof(ScopelessNode.RecordId)],
            entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(
            entity.GetKeys(),
            key => key
                .Properties
                .Select(property => property.Name)
                .SequenceEqual([nameof(ScopelessNode.NodeKey)]));
        Assert.Equal(nameof(ScopelessNode.NodeKey), entity.FindAnnotation("Doka:NestedSet:NodeKey")?.Value);
        Assert.Equal(nameof(ScopelessNode.TreeId), entity.FindAnnotation("Doka:NestedSet:TreeId")?.Value);
        Assert.Null(entity.FindAnnotation("Doka:NestedSet:Scope"));
        Assert.Equal(5, indexes.Length);
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual([nameof(ScopelessNode.TreeId), nameof(ScopelessNode.Left)]));
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual([nameof(ScopelessNode.TreeId), nameof(ScopelessNode.Right)]));
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
                [nameof(ScopelessNode.TreeId), nameof(ScopelessNode.ParentId), nameof(ScopelessNode.Position)]));

        var order = Assert.Single(
            indexes,
            index => index.Properties.SequenceEqual(
            [
                nameof(ScopelessNode.TreeId),
                nameof(ScopelessNode.ParentId),
                nameof(ScopelessNode.Name),
                nameof(ScopelessNode.Kind),
                nameof(ScopelessNode.NodeKey),
            ]));

        Assert.Equal(
            [
                false,
                false,
                false,
                true,
                false,
            ],
            order.Descending);
        Assert.All(indexes, index => Assert.DoesNotContain("Scope", index.Properties));
        Assert.Equal([nameof(ScopelessNode.ParentId)], parent.Properties.Select(property => property.Name));
        Assert.Equal([nameof(ScopelessNode.NodeKey)], parent.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        Assert.DoesNotContain(parent.Properties, property => property.Name == nameof(ScopelessNode.TreeId));
        Assert.Equal(
            4,
            entity
                .GetCheckConstraints()
                .Count());
    }

    /// <summary>Builds the scoped target model from options registration and one applied configuration.</summary>
    private sealed class ScopedContext : DbContext
    {
        /// <summary>Creates an isolated in-memory model without opening a database connection.</summary>
        internal ScopedContext() : base(
            new DbContextOptionsBuilder<ScopedContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.ApplyConfiguration(new ScopedNodeConfiguration());
    }

    /// <summary>Configures a scope-local scalar NodeKey over a composite EF primary key.</summary>
    private sealed class ScopedNodeConfiguration : IEntityTypeConfiguration<ScopedNode>
    {
        /// <inheritdoc />
        public void Configure(
            EntityTypeBuilder<ScopedNode> builder
        )
        {
            builder.HasKey(node => new
            {
                node.TenantId,
                node.RecordId,
            });
            builder.HasAlternateKey(node => new
            {
                node.TenantId,
                node.NodeKey,
            });
            builder.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(node => node.NodeKey)
                .HasScope(node => node.TenantId)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position)
                .OrderBy(node => node.Name)
                .ThenByDescending(node => node.Kind));
        }
    }

    /// <summary>Builds the scopeless target model from options registration and one applied configuration.</summary>
    private sealed class ScopelessContext : DbContext
    {
        /// <summary>Creates an isolated in-memory model without opening a database connection.</summary>
        internal ScopelessContext() : base(
            new DbContextOptionsBuilder<ScopelessContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.ApplyConfiguration(new ScopelessNodeConfiguration());
    }

    /// <summary>Configures an independent tree whose scalar NodeKey is an alternate key.</summary>
    private sealed class ScopelessNodeConfiguration : IEntityTypeConfiguration<ScopelessNode>
    {
        /// <inheritdoc />
        public void Configure(
            EntityTypeBuilder<ScopelessNode> builder
        )
        {
            builder.HasKey(node => node.RecordId);
            builder.HasAlternateKey(node => node.NodeKey);
            builder.HasNestedSet(nestedSet => nestedSet
                .HasNodeKey(node => node.NodeKey)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position)
                .OrderBy(node => node.Name)
                .ThenByDescending(node => node.Kind));
        }
    }

    /// <summary>Represents a scoped hierarchy entity whose NodeKey is separate from its composite EF key.</summary>
    private sealed class ScopedNode
    {
        /// <summary>Gets or sets the application partition.</summary>
        public int TenantId { get; set; }

        /// <summary>Gets or sets the scope-local record identity.</summary>
        public int RecordId { get; set; }

        /// <summary>Gets or sets the stable scalar hierarchy identity.</summary>
        public Guid NodeKey { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the zero-based depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the zero-based sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the primary sibling order value.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the descending sibling order value.</summary>
        public int Kind { get; set; }
    }

    /// <summary>Represents a scopeless hierarchy entity with a scalar alternate NodeKey.</summary>
    private sealed class ScopelessNode
    {
        /// <summary>Gets or sets the EF primary key.</summary>
        public int RecordId { get; set; }

        /// <summary>Gets or sets the stable scalar hierarchy identity.</summary>
        public Guid NodeKey { get; set; }

        /// <summary>Gets or sets the stable tree identity.</summary>
        public Guid TreeId { get; set; }

        /// <summary>Gets or sets the direct parent identity.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>Gets or sets the inclusive left boundary.</summary>
        public long Left { get; set; }

        /// <summary>Gets or sets the inclusive right boundary.</summary>
        public long Right { get; set; }

        /// <summary>Gets or sets the zero-based depth.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the zero-based sibling position.</summary>
        public long Position { get; set; }

        /// <summary>Gets or sets the primary sibling order value.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the descending sibling order value.</summary>
        public int Kind { get; set; }
    }
}
