namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the opt-in generated ancestor access path without opening a database connection.</summary>
public sealed partial class AncestorIndexTests
{
    /// <summary>
    /// Derives one additive index and provider-correct generated Boolean from final physical columns.
    /// </summary>
    /// <param name="engine">The provider whose real conventions build the model.</param>
    /// <param name="scoped">Whether scope participates in the index prefix.</param>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("Sqlite", true)]
    public void FindProperty_FinalColumns_ProduceGeneratedFlagAndAdditiveIndex(
        string engine,
        bool scoped
    )
    {
        // Arrange
        using var context = new AncestorContext(
            engine,
            model =>
            {
                var entity = Configure(model, scoped);
                entity.HasNestedSet(builder => builder.HasAncestorIndex());
                entity
                    .Property(node => node.Left)
                    .HasColumnName("left coordinate");

                entity
                    .Property(node => node.Right)
                    .HasColumnName("right coordinate");
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(AncestorNode))!;

        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;
        var descriptor = NestedSetModelDescriptor.FromFinalized(entity);

        // Assert
        Assert.True(flag.IsShadowProperty());
        Assert.Equal(typeof(bool), flag.ClrType);
        Assert.False(flag.IsNullable);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, flag.ValueGenerated);
        Assert.Equal(PropertySaveBehavior.Ignore, flag.GetBeforeSaveBehavior());
        Assert.Equal(PropertySaveBehavior.Ignore, flag.GetAfterSaveBehavior());
        Assert.Equal(engine == "PostgreSql", flag.GetIsStored());
        Assert.Contains("right coordinate", flag.GetComputedColumnSql(), StringComparison.Ordinal);
        Assert.Contains("left coordinate", flag.GetComputedColumnSql(), StringComparison.Ordinal);
        Assert.Contains("9223372036854775807", flag.GetComputedColumnSql(), StringComparison.Ordinal);
        Assert.Contains("CASE WHEN ", flag.GetComputedColumnSql(), StringComparison.Ordinal);

        Assert.Equal("NestedSetIsNonLeaf", descriptor.AncestorIndexProperty?.Name);
        Assert.DoesNotContain(descriptor.StructuralProperties, property => property.Name == flag.Name);
        var expected = scoped
            ? new[]
            {
                "Scope",
                "NestedSetIsNonLeaf",
                "TreeId",
                "Right",
                "Left",
                "Id",
            }
            : ["NestedSetIsNonLeaf", "TreeId", "Right", "Left", "Id"];

        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Any(property => property == flag));
        Assert.Equal(expected, index.Properties.Select(property => property.Name));
        Assert.False(index.IsUnique);
        Assert.Equal(
            engine == "PostgreSql"
                ? "\"TreeId\" IS NOT NULL"
                : null,
            index.GetFilter());

        Assert.Equal(
            3,
            entity
                .GetIndexes()
                .Count(index => index.Properties.Any(property => property.Name == "TreeId")
                    && !index.Properties.Contains(flag)));
    }

    /// <summary>Ordinary models receive neither the extra property nor its write and storage cost.</summary>
    [Fact]
    public void FinalizeModel_UnconfiguredHierarchy_HasNoGeneratedFlag()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model => Configure(model));

        // Act
        var entity = context.Model.FindEntityType(typeof(AncestorNode))!;

        // Assert
        Assert.Null(entity.FindProperty("NestedSetIsNonLeaf"));
        Assert.Null(
            NestedSetModelDescriptor.FromFinalized(entity)
                .AncestorIndexProperty);
    }

    /// <summary>Repeated opt-in and renamed library flags retain one current generated access path.</summary>
    [Fact]
    public void FinalizeModel_RenamingOwnedFlag_RemovesObsoletePropertyAndIndex()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.HasNestedSet(builder => builder.HasAncestorIndex("OldFlag"));
                entity.HasNestedSet(builder => builder.HasAncestorIndex("CurrentFlag"));
                entity.HasNestedSet(builder => builder.HasAncestorIndex("CurrentFlag"));
            });

        // Act
        var entity = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(AncestorNode))!;

        // Assert
        Assert.Null(entity.FindProperty("OldFlag"));
        Assert.NotNull(entity.FindProperty("CurrentFlag"));
        Assert.Single(entity.GetIndexes(), index => index.Properties.Any(property => property.Name == "CurrentFlag"));
    }

    /// <summary>
    /// Adopting a library flag through explicit application configuration preserves its old access path.
    /// </summary>
    [Fact]
    public void HasNestedSet_ApplicationAdoption_PreservesObsoleteFlagAndIndex()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = Configure(model);
        entity.HasNestedSet(builder => builder.HasAncestorIndex("OldFlag"));
        entity.Property<bool>("OldFlag");
        var userIndex = entity
            .HasIndex("Scope", "OldFlag", "TreeId", "Right", "Left", "Id")
            .Metadata;

        // Act
        entity.HasNestedSet(builder => builder.HasAncestorIndex("CurrentFlag"));

        // Assert
        Assert.NotNull(entity.Metadata.FindProperty("OldFlag"));
        Assert.Contains(userIndex, entity.Metadata.GetIndexes());
        Assert.Equal(
            4,
            entity
                .Metadata
                .GetIndexes()
                .Count());
    }

    /// <summary>An already mapped required Boolean can supply the flag while retaining its CLR member.</summary>
    [Fact]
    public void FindProperty_ExistingRequiredBoolean_IsAdoptedWithoutReplacement()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
            {
                var entity = Configure(model);
                entity.Property(node => node.NonLeaf);
                entity.HasNestedSet(builder => builder.HasAncestorIndex(nameof(AncestorNode.NonLeaf)));
            });

        // Act
        var flag = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(AncestorNode))!
            .FindProperty(nameof(AncestorNode.NonLeaf))!;

        // Assert
        Assert.False(flag.IsShadowProperty());
        Assert.Equal(
            "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql());

        Assert.Equal(ValueGenerated.OnAddOrUpdate, flag.ValueGenerated);
    }

    /// <summary>Application-specialized ancestor indexes survive a later flag name change unchanged.</summary>
    /// <param name="facet">The explicit application index customization.</param>
    [Theory]
    [InlineData("filter")]
    [InlineData("descending")]
    [InlineData("unique")]
    [InlineData("annotation")]
    public void HasNestedSet_CustomizedAncestorIndex_SurvivesFlagRemapping(
        string facet
    )
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = Configure(model);
        entity.HasNestedSet(builder => builder.HasAncestorIndex("OldFlag"));
        entity.Property<bool>("OldFlag");
        var customized = entity.HasIndex("Scope", "OldFlag", "TreeId", "Right", "Left", "Id");
        switch (facet)
        {
            case "filter":
                customized.HasFilter("\"OldFlag\" = 1");
                break;
            case "descending":
                customized.IsDescending();
                break;
            case "unique":
                customized.IsUnique();
                break;
            case "annotation":
                customized.HasAnnotation("Application:CustomIndex", true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(facet));
        }

        var metadata = customized.Metadata;
        var annotations = metadata
            .GetAnnotations()
            .ToArray();

        var descending = metadata.IsDescending?.ToArray();
        var unique = metadata.IsUnique;

        // Act
        entity.HasNestedSet(builder => builder.HasAncestorIndex("NewFlag"));

        // Assert
        Assert.Contains(metadata, entity.Metadata.GetIndexes());
        Assert.NotNull(entity.Metadata.FindProperty("OldFlag"));
        Assert.Equal(annotations, metadata.GetAnnotations());
        Assert.Equal(descending, metadata.IsDescending);
        Assert.Equal(unique, metadata.IsUnique);
        Assert.Equal(
            "NewFlag",
            entity.Metadata.FindAnnotation(NestedSetAnnotationNames.AncestorIndexProperty)
                ?.Value);
    }
}
