using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OrderingConfigurationTests
{
    /// <summary>Retains manual-position behavior when no domain ordering is configured.</summary>
    [Fact]
    public void For_UnconfiguredOrder_RemainsNull()
    {
        // Arrange
        using var context = new ConfigurationContext(null);

        // Act
        var mapping = NestedSetMapping<ConfigurationNode, int, int>.For(
            context,
            context.Model.FindEntityType(typeof(ConfigurationNode))!);

        // Assert
        Assert.Null(mapping.Order);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Completes mixed criteria with a unique ascending key and the default strict policy.</summary>
    [Fact]
    public void For_ConfiguredOrder_AppendsAscendingPrimaryKey()
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
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Requires nullable criteria to declare provider-independent null placement.</summary>
    [Fact]
    public void FinalizeModel_NullableCriterionWithoutNullPlacement_IsRejected()
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
    public void For_NullableCriterion_RetainsExplicitNullPlacement()
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
    public void FinalizeModel_RequiredCriterionWithNullPlacement_IsRejected()
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
    public void HasNestedSet_UndefinedNullPlacement_PreservesExistingConfiguration()
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
    public void FinalizeModel_ExplicitNodeKeyCriterion_IsRejected(
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
    public void For_ManualPlacementPolicy_IsRetained()
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
    public void For_UnsupportedSqliteCriterion_IsRejectedBeforeDatabaseAccess(
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
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Accepts a domain decimal when its explicit provider representation supports SQLite ordering.</summary>
    [Fact]
    public void For_ConvertedSqliteCriterion_UsesSupportedProviderRepresentation()
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

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Rejects criteria that can change again when hierarchy maintenance updates a stored row.</summary>
    /// <param name="generation">The update-time generation policy incompatible with stable ordering.</param>
    [Theory]
    [InlineData(ValueGenerated.OnUpdate)]
    [InlineData(ValueGenerated.OnAddOrUpdate)]
    public void For_UpdateGeneratedCriterion_IsRejectedBeforeDatabaseAccess(
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
    public void For_CriterionComputedFromStructure_IsRejected(
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
}
