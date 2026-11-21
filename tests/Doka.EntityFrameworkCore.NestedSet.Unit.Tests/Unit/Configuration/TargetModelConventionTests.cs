namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the target hierarchy roles, final validation, and deterministic index contract.</summary>
public sealed partial class TargetModelConventionTests
{
    /// <summary>Derives scoped access paths, constraints, and the scope-preserving parent relationship.</summary>
    [Fact]
    public void DesignEntity_ScopedOrderedModel_DerivesExactIndexes()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(
                entity,
                true,
                builder => builder
                    .OrderBy(node => node.Name)
                    .ThenByDescending(node => node.Kind));
        });

        // Act
        var entity = DesignEntity(context);
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

        var parents = entity
            .GetForeignKeys()
            .ToArray();

        var checks = entity
            .GetCheckConstraints()
            .Select(constraint => constraint.Sql)
            .OrderBy(expression => expression, StringComparer.Ordinal)
            .ToArray();

        var runtimeEntity = context.Model.FindEntityType(typeof(TargetNode))!;
        var descriptor = NestedSetModelMapping
            .For(context.Model)
            .Descriptor(runtimeEntity);

        // Assert
        var parent = Assert.Single(
            parents,
            foreignKey => foreignKey.Properties.Contains(entity.FindProperty(nameof(TargetNode.ParentId))!));

        Assert.Equal(5, indexes.Length);
        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
                [nameof(TargetNode.Scope), nameof(TargetNode.TreeId), nameof(TargetNode.Left)]));

        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
                [nameof(TargetNode.Scope), nameof(TargetNode.TreeId), nameof(TargetNode.Right)]));

        Assert.Contains(
            indexes,
            index => index.Properties.SequenceEqual(
            [
                nameof(TargetNode.Scope),
                nameof(TargetNode.TreeId),
                nameof(TargetNode.ParentId),
                nameof(TargetNode.Position),
            ]));

        var order = Assert.Single(
            indexes,
            index => index.Properties.SequenceEqual(
            [
                nameof(TargetNode.Scope),
                nameof(TargetNode.TreeId),
                nameof(TargetNode.ParentId),
                nameof(TargetNode.Name),
                nameof(TargetNode.Kind),
                nameof(TargetNode.NodeKey),
            ]));

        Assert.Equal([false, false, false, false, true, false], order.Descending);
        Assert.Equal(
            [nameof(TargetNode.Scope), nameof(TargetNode.ParentId)],
            parent.Properties.Select(property => property.Name));

        Assert.Equal(
            [nameof(TargetNode.Scope), nameof(TargetNode.NodeKey)],
            parent.PrincipalKey.Properties.Select(property => property.Name));

        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        Assert.DoesNotContain(parent.Properties, property => property.Name == nameof(TargetNode.TreeId));
        Assert.Equal(["\"Depth\" >= 0", "\"Left\" >= 1", "\"Position\" >= 0", "\"Right\" > \"Left\""], checks);
        Assert.All(
            indexes,
            index => Assert.False(
                entity
                    .GetIndexes()
                    .Single(candidate => candidate
                        .Properties
                        .Select(property => property.Name)
                        .SequenceEqual(index.Properties))
                    .IsUnique));

        Assert.Equal(nameof(TargetNode.Scope), descriptor.Scope?.Name);
        Assert.Equal(8, descriptor.StructuralProperties.Count);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Removes only scope from the access paths, identity key, and parent relationship.</summary>
    [Fact]
    public void DesignEntity_ScopelessModel_DerivesTreeLocalIndexes()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(entity, false, null);
        });

        // Act
        var entity = DesignEntity(context);
        var indexes = entity
            .GetIndexes()
            .Select(index => index
                .Properties
                .Select(property => property.Name)
                .ToArray())
            .ToArray();

        var parents = entity
            .GetForeignKeys()
            .ToArray();

        var runtimeEntity = context.Model.FindEntityType(typeof(TargetNode))!;
        var descriptor = NestedSetModelMapping
            .For(context.Model)
            .Descriptor(runtimeEntity);

        // Assert
        var parent = Assert.Single(
            parents,
            foreignKey => foreignKey.Properties.Contains(entity.FindProperty(nameof(TargetNode.ParentId))!));

        Assert.Equal(4, indexes.Length);
        Assert.Contains(
            indexes,
            properties => properties.SequenceEqual([nameof(TargetNode.TreeId), nameof(TargetNode.Left)]));

        Assert.Contains(
            indexes,
            properties => properties.SequenceEqual([nameof(TargetNode.TreeId), nameof(TargetNode.Right)]));

        Assert.Contains(
            indexes,
            properties => properties.SequenceEqual(
                [nameof(TargetNode.TreeId), nameof(TargetNode.ParentId), nameof(TargetNode.Position)]));

        Assert.Equal([nameof(TargetNode.ParentId)], parent.Properties.Select(property => property.Name));
        Assert.Equal([nameof(TargetNode.NodeKey)], parent.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        Assert.DoesNotContain(parent.Properties, property => property.Name == nameof(TargetNode.TreeId));
        Assert.Equal(
            4,
            entity
                .GetCheckConstraints()
                .Count());

        Assert.All(indexes, properties => Assert.DoesNotContain(nameof(TargetNode.Scope), properties));
        Assert.Null(descriptor.Scope);
        Assert.Equal(7, descriptor.StructuralProperties.Count);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Accepts a scope-qualified scalar NodeKey when the EF primary key is composite.</summary>
    [Fact]
    public void DesignEntity_CompositePrimaryKey_AcceptsScalarAlternateNodeKey()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => new
            {
                node.Scope,
                node.RecordId,
            });

            entity.HasAlternateKey(node => new
            {
                node.Scope,
                node.NodeKey,
            });

            Configure(entity, true, null);
        });

        // Act
        var entity = DesignEntity(context);

        // Assert
        Assert.Equal(2, entity.FindPrimaryKey()!.Properties.Count);
        Assert.Contains(
            entity.GetKeys(),
            key => key
                .Properties
                .Select(property => property.Name)
                .SequenceEqual([nameof(TargetNode.Scope), nameof(TargetNode.NodeKey)]));

        Assert.Equal(
            nameof(TargetNode.NodeKey),
            entity.FindAnnotation("Doka:NestedSet:NodeKey")
                ?.Value);

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Rejects an explicitly selected NodeKey that has no unique EF identity.</summary>
    [Fact]
    public void FinalizeModel_CompositePrimaryKey_RejectsNonUniqueNodeKey()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => new
            {
                node.Scope,
                node.RecordId,
            });

            Configure(entity, true, null);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("primary or alternate key", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Supports shadow structural properties through the string-based configuration surface.</summary>
    [Fact]
    public void FinalizeModel_ShadowStructuralRoles_AreValidatedAndIndexed()
    {
        // Arrange
        using var context = new ShadowModelContext();

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(ShadowNode))!;

        // Assert
        Assert.True(entity.FindProperty("Tree")!.IsShadowProperty());
        Assert.True(entity.FindProperty("LeftBoundary")!.IsShadowProperty());
        Assert.Equal(typeof(long), entity.FindProperty("LeftBoundary")!.ClrType);
        Assert.Contains(
            entity.GetIndexes(),
            index => index
                .Properties
                .Select(property => property.Name)
                .SequenceEqual(["Tree", "LeftBoundary"]));

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Creates one valid unscoped target model for metadata-derived negative controls.</summary>
    private static TargetModelContext ValidTargetContext()
    {
        return new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(entity, false, null);
        });
    }

    /// <summary>Creates one model with an application-owned check using a convention-generated name.</summary>
    private static TargetModelContext CheckConstraintContext(
        string name,
        string sql
    )
    {
        return new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(entity, false, null);
            entity.ToTable("TargetNode", table => table.HasCheckConstraint(name, sql));
        });
    }

    /// <summary>Configures the common target structure with an optional scope and optional sibling order.</summary>
    /// <param name="entity">The test entity builder.</param>
    /// <param name="scoped">Whether to configure the optional Scope role.</param>
    /// <param name="order">The optional sibling-order callback.</param>
    private static void Configure(
        EntityTypeBuilder<TargetNode> entity,
        bool scoped,
        Action<NestedSetBuilder<TargetNode>>? order
    )
    {
        entity.HasNestedSet(builder =>
        {
            builder
                .HasNodeKey(node => node.NodeKey)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentId)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position);

            if (scoped)
            {
                builder.HasScope(node => node.Scope);
            }

            order?.Invoke(builder);
        });
    }

    /// <summary>Returns finalized design metadata so provider conventions and physical names are observable.</summary>
    /// <param name="context">The isolated model context.</param>
    /// <returns>The finalized target entity metadata.</returns>
    private static IEntityType DesignEntity(
        TargetModelContext context
    ) => context
        .GetService<IDesignTimeModel>()
        .Model
        .FindEntityType(typeof(TargetNode))!;

    /// <summary>Creates provider options with convention registration and variant-aware model caching.</summary>
    /// <typeparam name="TContext">The context receiving the options.</typeparam>
    /// <returns>SQLite options that never open a physical connection during these tests.</returns>
    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets());

        if (typeof(ITestModelVariant).IsAssignableFrom(typeof(TContext)))
        {
            options.ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>();
        }

        return options.Options;
    }
}
