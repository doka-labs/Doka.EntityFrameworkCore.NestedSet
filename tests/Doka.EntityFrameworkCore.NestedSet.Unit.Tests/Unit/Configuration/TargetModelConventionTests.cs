namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the target hierarchy roles, final validation, and deterministic index contract.</summary>
public sealed class TargetModelConventionTests
{
    /// <summary>Derives scoped access paths, constraints, and the scope-preserving parent relationship.</summary>
    [Fact]
    public void ScopedOrderedModelDerivesExactIndexes()
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
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Removes only scope from the access paths, identity key, and parent relationship.</summary>
    [Fact]
    public void ScopelessModelDerivesTreeLocalIndexes()
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
        Assert.Equal(4, entity.GetCheckConstraints().Count());
        Assert.All(indexes, properties => Assert.DoesNotContain(nameof(TargetNode.Scope), properties));
        Assert.Null(descriptor.Scope);
        Assert.Equal(7, descriptor.StructuralProperties.Count);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Rejects a scoped parent relationship whose application-owned delete behavior can cascade.</summary>
    [Fact]
    public void ScopedCascadeParentRelationshipIsRejected()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => new
            {
                node.Scope,
                node.NodeKey,
            });

            entity
                .HasOne<TargetNode>()
                .WithMany()
                .HasForeignKey(nameof(TargetNode.Scope), nameof(TargetNode.ParentId))
                .HasPrincipalKey(nameof(TargetNode.Scope), nameof(TargetNode.NodeKey))
                .OnDelete(DeleteBehavior.Cascade);

            Configure(entity, true, null);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("Restrict or NoAction", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Preserves an application-owned NoAction parent relationship.</summary>
    [Fact]
    public void ScopedNoActionParentRelationshipIsPreserved()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => new
            {
                node.Scope,
                node.NodeKey,
            });

            entity
                .HasOne<TargetNode>()
                .WithMany()
                .HasForeignKey(nameof(TargetNode.Scope), nameof(TargetNode.ParentId))
                .HasPrincipalKey(nameof(TargetNode.Scope), nameof(TargetNode.NodeKey))
                .OnDelete(DeleteBehavior.NoAction);

            Configure(entity, true, null);
        });

        // Act
        var entity = DesignEntity(context);
        var parents = entity
            .GetForeignKeys()
            .ToArray();

        // Assert
        var parent = Assert.Single(
            parents,
            foreignKey => foreignKey.Properties.Contains(entity.FindProperty(nameof(TargetNode.ParentId))!));

        Assert.Equal(DeleteBehavior.NoAction, parent.DeleteBehavior);
    }

    /// <summary>Rejects a scopeless Parent role already bound to another principal identity.</summary>
    [Fact]
    public void ScopelessAmbiguousParentRelationshipIsRejected()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity.HasAlternateKey(node => node.OtherNodeKey);

            entity
                .HasOne<TargetNode>()
                .WithMany()
                .HasForeignKey(node => node.ParentId)
                .HasPrincipalKey(node => node.OtherNodeKey)
                .OnDelete(DeleteBehavior.Restrict);

            Configure(entity, false, null);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("ambiguous relationship", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Omits the convention self-FK when key and parent collations are physically incompatible.</summary>
    [Fact]
    public void IncompatibleParentCollationOmitsConventionForeignKey()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.Name);
            entity
                .Property(node => node.Name)
                .UseCollation("NOCASE");

            entity
                .Property(node => node.ParentName)
                .UseCollation("BINARY");

            entity.HasNestedSet(builder => builder
                .HasNodeKey(node => node.Name)
                .HasTreeId(node => node.TreeId)
                .HasParent(node => node.ParentName)
                .HasBounds(node => node.Left, node => node.Right)
                .HasDepth(node => node.Depth)
                .HasPosition(node => node.Position));
        });

        // Act
        var entity = DesignEntity(context);

        // Assert
        Assert.DoesNotContain(
            entity.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Contains(entity.FindProperty(nameof(TargetNode.ParentName))!));
        Assert.Equal(4, entity.GetCheckConstraints().Count());
    }

    /// <summary>Accepts a scope-qualified scalar NodeKey when the EF primary key is composite.</summary>
    [Fact]
    public void CompositePrimaryKeyAcceptsScalarAlternateNodeKey()
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
        Assert.Equal(nameof(TargetNode.NodeKey), entity.FindAnnotation("Doka:NestedSet:NodeKey")?.Value);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Rejects an explicitly selected NodeKey that has no unique EF identity.</summary>
    [Fact]
    public void CompositePrimaryKeyRejectsNonUniqueNodeKey()
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
    public void ShadowStructuralRolesAreValidatedAndIndexed()
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
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Rejects Int32 boundaries selected through the untyped shadow-property surface.</summary>
    [Fact]
    public void Int32ShadowBoundaryIsRejectedAtFinalization()
    {
        // Arrange
        using var context = new InvalidShadowModelContext();

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("must be required Int64", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a TreeId whose value is generated instead of assigned by the hierarchy protocol.</summary>
    [Fact]
    public void GeneratedTreeIdIsRejectedAtFinalization()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(entity, false, null);
            entity
                .Property(node => node.TreeId)
                .ValueGeneratedOnAdd();
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("TreeId", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("writable and not generated", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a TreeId that EF treats as read-only after the node was inserted.</summary>
    [Fact]
    public void ReadOnlyTreeIdIsRejectedAtFinalization()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            Configure(entity, false, null);
            entity
                .Property(node => node.TreeId)
                .Metadata
                .SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("TreeId", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("writable and not generated", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects every invalid structural role type or nullable arithmetic role with a precise diagnosis.</summary>
    /// <param name="variant">The deliberately invalid role mapping.</param>
    /// <param name="role">The structural role expected in the diagnostic.</param>
    /// <param name="diagnostic">The role-specific contract fragment expected in the diagnostic.</param>
    [Theory]
    [InlineData("parent-string", "Parent", "nullable Parent property")]
    [InlineData("right-int", "Right", "required Int64")]
    [InlineData("depth-long", "Depth", "required Int32")]
    [InlineData("position-int", "Position", "required Int64")]
    [InlineData("tree-nullable", "TreeId", "must be required")]
    [InlineData("left-nullable", "Left", "required Int64")]
    [InlineData("right-nullable", "Right", "required Int64")]
    [InlineData("depth-nullable", "Depth", "required Int32")]
    [InlineData("position-nullable", "Position", "required Int64")]
    public void InvalidStructuralRoleTypeIsRejectedAtFinalization(
        string variant,
        string role,
        string diagnostic
    )
    {
        // Arrange
        using var context = new InvalidRoleModelContext(variant);

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(role, invalid.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostic, invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects generated and read-only structural roles before a database connection is opened.</summary>
    /// <param name="variant">The generated or read-only role mapping.</param>
    /// <param name="role">The structural role expected in the diagnostic.</param>
    [Theory]
    [InlineData("generated-parent", "Parent")]
    [InlineData("generated-left", "Left")]
    [InlineData("generated-right", "Right")]
    [InlineData("generated-depth", "Depth")]
    [InlineData("generated-position", "Position")]
    [InlineData("readonly-parent", "Parent")]
    [InlineData("readonly-left", "Left")]
    [InlineData("readonly-right", "Right")]
    [InlineData("readonly-depth", "Depth")]
    [InlineData("readonly-position", "Position")]
    public void NonWritableStructuralRoleIsRejectedAtFinalization(
        string variant,
        string role
    )
    {
        // Arrange
        using var context = new InvalidRoleModelContext(variant);

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(role, invalid.Message, StringComparison.Ordinal);
        Assert.Contains("writable and not generated", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a generated Scope while accepting EF key immutability after insertion.</summary>
    [Fact]
    public void GeneratedScopeIsRejectedAtFinalization()
    {
        // Arrange
        using var context = new InvalidRoleModelContext("generated-scope");

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("Scope", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("assigned value and not be generated", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects an application-managed concurrency token without an explicit structural-write policy.</summary>
    [Fact]
    public void ApplicationManagedConcurrencyTokenRequiresPolicy()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity
                .Property(node => node.Revision)
                .IsConcurrencyToken();

            Configure(entity, false, null);
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(nameof(TargetNode.Revision), invalid.Message, StringComparison.Ordinal);
        Assert.Contains(
            nameof(NestedSetBuilder<>.PreserveApplicationConcurrencyTokens),
            invalid.Message,
            StringComparison.Ordinal);
    }

    /// <summary>Accepts an explicit decision to preserve application-managed tokens during structure-only writes.</summary>
    [Fact]
    public void ApplicationManagedConcurrencyTokenCanBePreserved()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity
                .Property(node => node.Revision)
                .IsConcurrencyToken();

            Configure(entity, false, builder => builder.PreserveApplicationConcurrencyTokens());
        });

        // Act
        var entity = DesignEntity(context);

        // Assert
        Assert.True(entity.FindProperty(nameof(TargetNode.Revision))!.IsConcurrencyToken);
        Assert.Equal(true, entity.FindAnnotation(NestedSetAnnotationNames.PreserveApplicationConcurrencyTokens)!.Value);
    }

    /// <summary>Accepts store-generated concurrency tokens without an application-managed-token policy.</summary>
    [Fact]
    public void StoreGeneratedConcurrencyTokenDoesNotRequirePolicy()
    {
        // Arrange
        using var context = new TargetModelContext(entity =>
        {
            entity.HasKey(node => node.RecordId);
            entity.HasAlternateKey(node => node.NodeKey);
            entity
                .Property(node => node.Revision)
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            Configure(entity, false, null);
        });

        // Act
        var entity = DesignEntity(context);

        // Assert
        Assert.True(entity.FindProperty(nameof(TargetNode.Revision))!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, entity.FindProperty(nameof(TargetNode.Revision))!.ValueGenerated);
    }

    /// <summary>Reuses an application check that has the generated name and identical SQL.</summary>
    [Fact]
    public void EquivalentNamedCheckConstraintIsReused()
    {
        // Arrange
        using var baseline = ValidTargetContext();
        var leftCheck = DesignEntity(baseline)
            .GetCheckConstraints()
            .Single(constraint => constraint.Sql == "\"Left\" >= 1");

        var leftCheckName = leftCheck.Name
            ?? throw new InvalidOperationException("The generated Left check constraint did not have a physical name.");

        using var context = CheckConstraintContext(leftCheckName, leftCheck.Sql);

        // Act
        var entity = DesignEntity(context);

        // Assert
        var actual = Assert.Single(entity.GetCheckConstraints(), constraint => constraint.Name == leftCheckName);
        Assert.Equal(leftCheck.Sql, actual.Sql);
    }

    /// <summary>Rejects an application check that reuses a generated name for different SQL.</summary>
    [Fact]
    public void ConflictingNamedCheckConstraintIsRejected()
    {
        // Arrange
        using var baseline = ValidTargetContext();
        var leftCheck = DesignEntity(baseline)
            .GetCheckConstraints()
            .Single(constraint => constraint.Sql == "\"Left\" >= 1");

        var leftCheckName = leftCheck.Name
            ?? throw new InvalidOperationException("The generated Left check constraint did not have a physical name.");

        using var context = CheckConstraintContext(leftCheckName, "\"Left\" >= 0");

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains(leftCheckName, invalid.Message, StringComparison.Ordinal);
        Assert.Contains("conflicts with application SQL", invalid.Message, StringComparison.Ordinal);
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
            entity.Property(Variant == "tree-nullable" ? typeof(Guid?) : typeof(Guid), "Tree");
            entity.Property(Variant == "parent-string" ? typeof(string) : typeof(int?), "Parent");
            entity.Property(Variant == "left-nullable" ? typeof(long?) : typeof(long), "LeftBoundary");
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

    /// <summary>Creates provider options with convention registration and variant-aware model caching.</summary>
    /// <typeparam name="TContext">The context receiving the options.</typeparam>
    /// <returns>SQLite options that never open a physical connection during these tests.</returns>
    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets();

        if (typeof(ITestModelVariant).IsAssignableFrom(typeof(TContext)))
        {
            options.ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>();
        }

        return options.Options;
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
