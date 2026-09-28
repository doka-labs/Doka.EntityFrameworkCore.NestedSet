namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies sibling-order configuration, isolated model metadata, and immutable runtime mappings.</summary>
public sealed partial class OrderingConfigurationTests
{
    /// <summary>Rejects selectors that cannot identify one direct stored entity property.</summary>
    /// <param name="variant">The selector shape to reject.</param>
    /// <param name="expectedException">The configuration failure expected for the selector.</param>
    [Theory]
    [InlineData("computed", typeof(ArgumentException))]
    [InlineData("nested", typeof(ArgumentException))]
    [InlineData("boxed", typeof(ArgumentException))]
    [InlineData("null", typeof(ArgumentNullException))]
    public void InvalidSelectorPreservesExistingConfiguration(
        string variant,
        Type expectedException
    )
    {
        // Arrange
        var entity = CreateEntity(true);
        var annotations = SnapshotAnnotations(entity);
        var indexes = entity
            .Metadata
            .GetIndexes()
            .ToArray();

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder => SelectInvalid(builder, variant)));

        // Assert
        Assert.IsType(expectedException, exception);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
        Assert.Equal(
            indexes,
            entity
                .Metadata
                .GetIndexes()
                .ToArray());
    }

    /// <summary>Rejects ordering by every structural role whose value is owned by hierarchy maintenance.</summary>
    /// <param name="property">The structural property selected as an ordering criterion.</param>
    [Theory]
    [InlineData(nameof(ConfigurationNode.Left))]
    [InlineData(nameof(ConfigurationNode.Right))]
    [InlineData(nameof(ConfigurationNode.Depth))]
    [InlineData(nameof(ConfigurationNode.Position))]
    [InlineData(nameof(ConfigurationNode.ParentId))]
    [InlineData(nameof(ConfigurationNode.Scope))]
    [InlineData(nameof(ConfigurationNode.TreeId))]
    public void StructuralCriterionPreservesExistingConfiguration(
        string property
    )
    {
        // Arrange
        var entity = CreateEntity(true);
        var annotations = SnapshotAnnotations(entity);
        var indexes = entity
            .Metadata
            .GetIndexes()
            .ToArray();

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder => SelectStructural(builder, property)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
        Assert.Equal(
            indexes,
            entity
                .Metadata
                .GetIndexes()
                .ToArray());
    }

    /// <summary>Rejects a direct CLR property that has explicitly been excluded from the EF model.</summary>
    [Fact]
    public void UnmappedCriterionPreservesExistingConfiguration()
    {
        // Arrange
        var entity = CreateEntity(true);
        entity.Ignore(node => node.Unmapped);
        var annotations = SnapshotAnnotations(entity);
        var indexes = entity
            .Metadata
            .GetIndexes()
            .ToArray();

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder => builder.OrderBy(node => node.Unmapped)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
        Assert.Equal(
            indexes,
            entity
                .Metadata
                .GetIndexes()
                .ToArray());
        Assert.Null(entity.Metadata.FindProperty(nameof(ConfigurationNode.Unmapped)));
    }

    /// <summary>Rejects repeated criteria even when their requested directions differ.</summary>
    [Fact]
    public void DuplicateCriterionPreservesExistingConfiguration()
    {
        // Arrange
        var entity = CreateEntity(true);
        var annotations = SnapshotAnnotations(entity);

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder =>
            builder.ThenByDescending(node => node.Name)));

        // Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
    }

    /// <summary>Requires a primary order before either kind of secondary ordering can be selected.</summary>
    /// <param name="descending">Whether the attempted secondary criterion is descending.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThenByRequiresAnExistingOrder(
        bool descending
    )
    {
        // Arrange
        var entity = CreateEntity(false);
        var annotations = SnapshotAnnotations(entity);

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder =>
        {
            if (descending)
            {
                builder.ThenByDescending(node => node.Name);
            }
            else
            {
                builder.ThenBy(node => node.Name);
            }
        }));

        // Assert
        Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
    }

    /// <summary>Rejects a placement policy without an ordering contract to which it can apply.</summary>
    [Fact]
    public void OrderModeRequiresACriterion()
    {
        // Arrange
        var entity = CreateEntity(false);
        var annotations = SnapshotAnnotations(entity);

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder =>
            builder.HasOrderMode(NestedSetOrderMode.AllowManualPlacement)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
    }

    /// <summary>Rejects enum values that would otherwise create an undefined placement policy.</summary>
    /// <param name="mode">An undefined enum value.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void UndefinedOrderModePreservesExistingConfiguration(
        int mode
    )
    {
        // Arrange
        var entity = CreateEntity(true);
        var annotations = SnapshotAnnotations(entity);

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder =>
            builder.HasOrderMode((NestedSetOrderMode)mode)));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
    }

    /// <summary>Discards all pending order and policy changes when the application callback fails.</summary>
    [Fact]
    public void FailedCallbackRetainsExistingOrderAndIndexes()
    {
        // Arrange
        var entity = CreateEntity(true);
        var annotations = SnapshotAnnotations(entity);
        var indexes = entity
            .Metadata
            .GetIndexes()
            .ToArray();

        var failure = new InvalidOperationException("The configuration callback failed.");

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder =>
        {
            builder
                .OrderByDescending(node => node.Rank)
                .HasOrderMode(NestedSetOrderMode.AllowManualPlacement);

            throw failure;
        }));

        // Assert
        Assert.Same(failure, exception);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
        Assert.Equal(
            indexes,
            entity
                .Metadata
                .GetIndexes()
                .ToArray());
    }

    /// <summary>Checks pending structural roles before any part of a replacement mapping is applied.</summary>
    [Fact]
    public void InvalidCompletedOrderRetainsPreviousStructuralMapping()
    {
        // Arrange
        var entity = CreateEntity(true);
        var annotations = SnapshotAnnotations(entity);
        var indexes = entity
            .Metadata
            .GetIndexes()
            .ToArray();

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder => builder
            .HasDepth(node => node.Rank)
            .OrderBy(node => node.Rank)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
        Assert.Equal(
            indexes,
            entity
                .Metadata
                .GetIndexes()
                .ToArray());
    }

    /// <summary>Retains manual-position behavior when no domain ordering is configured.</summary>
    [Fact]
    public void UnconfiguredOrderRemainsNull()
    {
        // Arrange
        using var context = new ConfigurationContext(null);

        // Act
        var mapping = NestedSetMapping<ConfigurationNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(ConfigurationNode))!);

        // Assert
        Assert.Null(mapping.Order);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Completes mixed criteria with a unique ascending key and the default strict policy.</summary>
    [Fact]
    public void ConfiguredOrderAppendsAscendingPrimaryKey()
    {
        // Arrange
        using var context = new ConfigurationContext(builder => builder
            .OrderBy(node => node.Name)
            .ThenByDescending(node => node.Rank));

        // Act
        var order = NestedSetMapping<ConfigurationNode, int, int>.For(
                context,
                context.Model.FindEntityType(typeof(ConfigurationNode))!)
            .Order;

        // Assert
        Assert.NotNull(order);
        Assert.Equal(
            new[] { nameof(ConfigurationNode.Name), nameof(ConfigurationNode.Rank), nameof(ConfigurationNode.Id) },
            order.Properties.Select(property => property.Name));

        Assert.Equal<bool>([false, true, false], order.Descending);
        Assert.Equal(NestedSetOrderMode.Strict, order.Mode);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Requires nullable criteria to declare provider-independent null placement.</summary>
    [Fact]
    public void NullableCriterionWithoutNullPlacementIsRejected()
    {
        // Arrange
        using var context = new ConfigurationContext(builder => builder.OrderBy(node => node.OptionalCode));

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("requires explicit null placement", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Retains explicit null placement and leaves the required key tie breaker without null policy.</summary>
    [Fact]
    public void NullableCriterionRetainsExplicitNullPlacement()
    {
        // Arrange
        using var context = new ConfigurationContext(builder => builder
            .OrderByDescending(node => node.OptionalCode, NullSortOrder.First)
            .ThenBy(node => node.Name));

        // Act
        var order = NestedSetMapping<ConfigurationNode, int, int>.For(
                context,
                context.Model.FindEntityType(typeof(ConfigurationNode))!)
            .Order;

        // Assert
        Assert.NotNull(order);
        Assert.Equal<NullSortOrder?>([NullSortOrder.First, null, null], order.NullSortOrders);
        Assert.Equal<bool>([true, false, false], order.Descending);
    }

    /// <summary>Rejects null placement on required properties because it cannot affect their order.</summary>
    [Fact]
    public void RequiredCriterionWithNullPlacementIsRejected()
    {
        // Arrange
        using var context = new ConfigurationContext(builder => builder.OrderBy(node => node.Name, NullSortOrder.Last));

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("must not configure null placement", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects undefined null-placement values before publishing any order metadata.</summary>
    [Fact]
    public void UndefinedNullPlacementPreservesExistingConfiguration()
    {
        // Arrange
        var entity = CreateEntity(true);
        var annotations = SnapshotAnnotations(entity);

        // Act
        var exception = Record.Exception(() => entity.HasNestedSet(builder =>
            builder.OrderBy(node => node.OptionalCode, (NullSortOrder)2)));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(annotations, SnapshotAnnotations(entity));
    }

    /// <summary>Rejects NodeKey as a domain criterion because the library appends it ascending.</summary>
    /// <param name="keyOnly">Whether the key is the first and only configured criterion.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitNodeKeyCriterionIsRejected(
        bool keyOnly
    )
    {
        // Arrange
        using var context = new ConfigurationContext(builder =>
        {
            if (keyOnly)
            {
                builder.OrderByDescending(node => node.Id);
            }
            else
            {
                builder
                    .OrderBy(node => node.Name)
                    .ThenByDescending(node => node.Id);
            }
        });

        // Act
        var exception = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("structural properties", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Applies an explicitly selected manual-placement policy to the finalized order metadata.</summary>
    [Fact]
    public void ManualPlacementPolicyIsRetained()
    {
        // Arrange
        using var context = new ConfigurationContext(builder => builder
            .HasOrderMode(NestedSetOrderMode.AllowManualPlacement)
            .OrderBy(node => node.Name));

        // Act
        var order = NestedSetMapping<ConfigurationNode, int, int>.For(
                context,
                context.Model.FindEntityType(typeof(ConfigurationNode))!)
            .Order;

        // Assert
        Assert.NotNull(order);
        Assert.Equal(NestedSetOrderMode.AllowManualPlacement, order.Mode);
    }

    /// <summary>Rejects SQLite criteria without shared ordering semantics across EF queries and raw windows.</summary>
    /// <param name="property">The unsupported domain property selected for sibling ordering.</param>
    [Theory]
    [InlineData(nameof(ConfigurationNode.Price))]
    [InlineData(nameof(ConfigurationNode.Timestamp))]
    [InlineData(nameof(ConfigurationNode.Duration))]
    [InlineData(nameof(ConfigurationNode.Sequence))]
    public void UnsupportedSqliteCriterionIsRejectedBeforeDatabaseAccess(
        string property
    )
    {
        // Arrange
        using var context = new ConfigurationContext(builder => SelectUnsupportedSqlite(builder, property));

        // Act
        var exception = Record.Exception(() => NestedSetMapping<ConfigurationNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(ConfigurationNode))!));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("SQLite", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Accepts a domain decimal when its explicit provider representation supports SQLite ordering.</summary>
    [Fact]
    public void ConvertedSqliteCriterionUsesSupportedProviderRepresentation()
    {
        // Arrange
        using var context = new ConfigurationContext(
            builder => builder.OrderBy(node => node.Price),
            configureProperties: entity => entity
                .Property(node => node.Price)
                .HasConversion<string>());

        // Act
        var order = NestedSetMapping<ConfigurationNode, int, int>.For(
                context,
                context.Model.FindEntityType(typeof(ConfigurationNode))!)
            .Order;

        // Assert
        Assert.NotNull(order);
        Assert.Equal(nameof(ConfigurationNode.Price), order.Properties[0].Name);
        Assert.Equal(
            typeof(string),
            order
                .Properties[0]
                .GetTypeMapping()
                .Converter
                ?.ProviderClrType);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Rejects criteria that can change again when hierarchy maintenance updates a stored row.</summary>
    /// <param name="generation">The update-time generation policy incompatible with stable ordering.</param>
    [Theory]
    [InlineData(ValueGenerated.OnUpdate)]
    [InlineData(ValueGenerated.OnAddOrUpdate)]
    public void UpdateGeneratedCriterionIsRejectedBeforeDatabaseAccess(
        ValueGenerated generation
    )
    {
        // Arrange
        using var context = new ConfigurationContext(
            builder => builder.OrderBy(node => node.Rank),
            configureProperties: entity => entity.Property(node => node.Rank)
                .Metadata.ValueGenerated = generation);

        // Act
        var exception = Record.Exception(() => NestedSetMapping<ConfigurationNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(ConfigurationNode))!));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("generated on update", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Rejects a computed criterion whose value moves with its own structural coordinates.</summary>
    /// <param name="stored">Whether SQLite persists the computed column or calculates it on access.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CriterionComputedFromStructureIsRejected(
        bool stored
    )
    {
        // Arrange
        using var context = new ConfigurationContext(
            builder => builder.OrderBy(node => node.Rank),
            configureProperties: entity => entity
                .Property(node => node.Rank)
                .HasComputedColumnSql("-\"Left\"", stored));

        // Act
        var exception = Record.Exception(() => NestedSetMapping<ConfigurationNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(ConfigurationNode))!));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception, exactMatch: false);
        Assert.Contains("generated on update", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Allows an insert-generated criterion that remains stable during later hierarchy updates.</summary>
    [Fact]
    public void InsertGeneratedCriterionIsAccepted()
    {
        // Arrange
        using var context = new ConfigurationContext(
            builder => builder.OrderBy(node => node.Rank),
            configureProperties: entity => entity
                .Property(node => node.Rank)
                .HasDefaultValue(0)
                .ValueGeneratedOnAdd());

        // Act
        var order = NestedSetMapping<ConfigurationNode, int, int>.For(
                context,
                context.Model.FindEntityType(typeof(ConfigurationNode))!)
            .Order;

        // Assert
        Assert.NotNull(order);
        Assert.Equal(nameof(ConfigurationNode.Rank), order.Properties[0].Name);
        Assert.Equal(ValueGenerated.OnAdd, order.Properties[0].ValueGenerated);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    /// <summary>Prevents a consumer from changing the properties retained by a shared model mapping.</summary>
    [Fact]
    public void OrderPropertyCollectionRejectsMutation()
    {
        // Arrange
        using var context = new ConfigurationContext(builder => builder.OrderBy(node => node.Name));
        var order = NestedSetMapping<ConfigurationNode, int, int>.For(
                context,
                context.Model.FindEntityType(typeof(ConfigurationNode))!)
            .Order!;

        var properties = (IList<IProperty>)order.Properties;
        var replacement = order.Properties[^1];

        // Act
        var exception = Record.Exception(() => properties[0] = replacement);

        // Assert
        Assert.IsType<NotSupportedException>(exception);
        Assert.Equal(nameof(ConfigurationNode.Name), order.Properties[0].Name);
        Assert.True(properties.IsReadOnly);
    }

    /// <summary>Prevents a consumer from changing a cached comparison direction.</summary>
    [Fact]
    public void OrderDirectionCollectionRejectsMutation()
    {
        // Arrange
        using var context = new ConfigurationContext(builder => builder.OrderBy(node => node.Name));
        var order = NestedSetMapping<ConfigurationNode, int, int>.For(
                context,
                context.Model.FindEntityType(typeof(ConfigurationNode))!)
            .Order!;

        var descending = (IList<bool>)order.Descending;

        // Act
        var exception = Record.Exception(() => descending[0] = true);

        // Assert
        Assert.IsType<NotSupportedException>(exception);
        Assert.False(order.Descending[0]);
        Assert.True(descending.IsReadOnly);
    }

    /// <summary>Shares immutable mappings only when contexts use the same finalized EF model.</summary>
    [Fact]
    public async Task ContextsUsingTheSameModelReuseTheirMapping()
    {
        // Arrange
        var identity = Guid.NewGuid();

        // WHY: EF's bounded model cache also stores queries; unrelated suite work can evict a shared model.
        // This experiment owns one ordinary service graph so it measures mapping reuse for the same model.
        await using var services = new ServiceCollection()
            .AddEntityFrameworkSqlite()
            .AddSingleton<IModelCacheKeyFactory, TestModelCacheKeyFactory>()
            .BuildServiceProvider();

        await using var firstContext = new ConfigurationContext(
            builder => builder.OrderBy(node => node.Name),
            identity,
            services: services);

        await using var secondContext = new ConfigurationContext(
            builder => builder.OrderBy(node => node.Name),
            identity,
            services: services);

        // Act
        var first = NestedSetMapping<ConfigurationNode, int, int>.For(
            firstContext,
            firstContext.Model.FindEntityType(typeof(ConfigurationNode))!);

        var second = NestedSetMapping<ConfigurationNode, int, int>.For(
            secondContext,
            secondContext.Model.FindEntityType(typeof(ConfigurationNode))!);

        // Assert
        Assert.Same(firstContext.Model, secondContext.Model);
        Assert.Same(first, second);
        Assert.Same(first.Order, second.Order);
    }

    /// <summary>Keeps different ordering contracts isolated even when their entity CLR type is identical.</summary>
    [Fact]
    public void DistinctModelsRetainTheirOwnOrdering()
    {
        // Arrange
        using var firstContext = new ConfigurationContext(builder => builder.OrderBy(node => node.Name));
        using var secondContext = new ConfigurationContext(builder => builder.OrderByDescending(node => node.Rank));

        // Act
        var first = NestedSetMapping<ConfigurationNode, int, int>.For(
            firstContext,
            firstContext.Model.FindEntityType(typeof(ConfigurationNode))!);

        var second = NestedSetMapping<ConfigurationNode, int, int>.For(
            secondContext,
            secondContext.Model.FindEntityType(typeof(ConfigurationNode))!);

        // Assert
        Assert.NotSame(firstContext.Model, secondContext.Model);
        Assert.NotSame(first, second);
        Assert.NotNull(first.Order);
        Assert.NotNull(second.Order);
        Assert.Equal(nameof(ConfigurationNode.Name), first.Order.Properties[0].Name);
        Assert.False(first.Order.Descending[0]);
        Assert.Equal(nameof(ConfigurationNode.Rank), second.Order.Properties[0].Name);
        Assert.True(second.Order.Descending[0]);
    }
}
