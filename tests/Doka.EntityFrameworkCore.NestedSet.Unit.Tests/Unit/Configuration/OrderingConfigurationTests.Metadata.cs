using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class OrderingConfigurationTests
{
    /// <summary>Allows an insert-generated criterion that remains stable during later hierarchy updates.</summary>
    [Fact]
    public void For_InsertGeneratedCriterion_IsAccepted()
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
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Prevents a consumer from changing the properties retained by a shared model mapping.</summary>
    [Fact]
    public void Order_ChangingPropertyCollection_IsRejected()
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
    public void Order_ChangingDirectionCollection_IsRejected()
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
    public async Task For_ContextsUsingTheSameModel_ReuseTheirMapping()
    {
        // Arrange
        var identity = Guid.NewGuid();

        // WHY: EF's bounded model cache also stores queries; unrelated suite work can evict a shared model.
        // This experiment owns one ordinary service graph so it measures mapping reuse for the same model.
        await using var services = NestedSetTestAdapters.BuildServices(
            ConfigurationContext
                .CreateOptions()
                .Options,
            collection => collection.Replace(
                ServiceDescriptor.Singleton<IModelCacheKeyFactory, TestModelCacheKeyFactory>()));

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
    public void For_DistinctModels_RetainTheirOwnOrdering()
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
