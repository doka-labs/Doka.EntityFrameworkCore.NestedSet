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
    public void HasNestedSet_InvalidSelector_PreservesExistingConfiguration(
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
    public void HasNestedSet_StructuralCriterion_PreservesExistingConfiguration(
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
    public void HasNestedSet_UnmappedCriterion_PreservesExistingConfiguration()
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
    public void HasNestedSet_DuplicateCriterion_PreservesExistingConfiguration()
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
    public void HasNestedSet_ThenBy_RequiresAnExistingOrder(
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
    public void HasNestedSet_OrderMode_RequiresACriterion()
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
    public void HasNestedSet_UndefinedOrderMode_PreservesExistingConfiguration(
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
    public void HasNestedSet_FailedCallback_RetainsExistingOrderAndIndexes()
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
    public void HasNestedSet_InvalidCompletedOrder_RetainsPreviousStructuralMapping()
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
}
