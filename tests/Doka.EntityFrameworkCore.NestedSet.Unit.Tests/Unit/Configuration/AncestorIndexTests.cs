namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies the opt-in generated ancestor access path without opening a database connection.</summary>
public sealed partial class AncestorIndexTests
{
    /// <summary>Derives one additive index and provider-correct generated Boolean from final physical columns.</summary>
    /// <param name="engine">The provider whose real conventions build the model.</param>
    /// <param name="scoped">Whether scope participates in the index prefix.</param>
    [Theory]
    [InlineData("MySql", true)]
    [InlineData("MariaDb", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("SqlServer", false)]
    [InlineData("Sqlite", true)]
    public void FinalColumnsProduceGeneratedFlagAndAdditiveIndex(
        string engine,
        bool scoped
    )
    {
        // Arrange
        using var context = new AncestorContext(engine, model =>
        {
            var entity = Configure(model, scoped);
            entity.HasNestedSet(builder => builder.HasAncestorIndex());
            entity.Property(node => node.Left).HasColumnName("left coordinate");
            entity.Property(node => node.Right).HasColumnName("right coordinate");
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AncestorNode))!;
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
            ? new[] { "Scope", "NestedSetIsNonLeaf", "TreeId", "Right", "Left", "Id" }
            : ["NestedSetIsNonLeaf", "TreeId", "Right", "Left", "Id"];

        var index = Assert.Single(entity.GetIndexes(), index => index.Properties.Any(property => property == flag));
        Assert.Equal(expected, index.Properties.Select(property => property.Name));
        Assert.False(index.IsUnique);
        Assert.Equal(engine == "PostgreSql" ? "\"TreeId\" IS NOT NULL" : null, index.GetFilter());
        Assert.Equal(3, entity.GetIndexes().Count(index => index.Properties.Any(property => property.Name == "TreeId")
            && !index.Properties.Contains(flag)));
    }

    /// <summary>Ordinary models receive neither the extra property nor its write and storage cost.</summary>
    [Fact]
    public void UnconfiguredHierarchyHasNoGeneratedFlag()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model => Configure(model));

        // Act
        var entity = context.Model.FindEntityType(typeof(AncestorNode))!;

        // Assert
        Assert.Null(entity.FindProperty("NestedSetIsNonLeaf"));
        Assert.Null(NestedSetModelDescriptor.FromFinalized(entity).AncestorIndexProperty);
    }

    /// <summary>Repeated opt-in and renamed library flags retain one current generated access path.</summary>
    [Fact]
    public void RenamingOwnedFlagRemovesObsoletePropertyAndIndex()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.HasNestedSet(builder => builder.HasAncestorIndex("OldFlag"));
            entity.HasNestedSet(builder => builder.HasAncestorIndex("CurrentFlag"));
            entity.HasNestedSet(builder => builder.HasAncestorIndex("CurrentFlag"));
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AncestorNode))!;

        // Assert
        Assert.Null(entity.FindProperty("OldFlag"));
        Assert.NotNull(entity.FindProperty("CurrentFlag"));
        Assert.Single(entity.GetIndexes(), index => index.Properties.Any(property => property.Name == "CurrentFlag"));
    }

    /// <summary>Adopting a library flag through explicit application configuration preserves its old access path.</summary>
    [Fact]
    public void ApplicationAdoptionPreservesObsoleteFlagAndIndex()
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = Configure(model);
        entity.HasNestedSet(builder => builder.HasAncestorIndex("OldFlag"));
        entity.Property<bool>("OldFlag");
        var userIndex = entity.HasIndex("Scope", "OldFlag", "TreeId", "Right", "Left", "Id").Metadata;

        // Act
        entity.HasNestedSet(builder => builder.HasAncestorIndex("CurrentFlag"));

        // Assert
        Assert.NotNull(entity.Metadata.FindProperty("OldFlag"));
        Assert.Contains(userIndex, entity.Metadata.GetIndexes());
        Assert.Equal(4, entity.Metadata.GetIndexes().Count());
    }

    /// <summary>An already mapped required Boolean can supply the flag while retaining its CLR member.</summary>
    [Fact]
    public void ExistingRequiredBooleanIsAdoptedWithoutReplacement()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.Property(node => node.NonLeaf);
            entity.HasNestedSet(builder => builder.HasAncestorIndex(nameof(AncestorNode.NonLeaf)));
        });

        // Act
        var flag = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AncestorNode))!
            .FindProperty(nameof(AncestorNode.NonLeaf))!;

        // Assert
        Assert.False(flag.IsShadowProperty());
        Assert.Equal("\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\" ELSE \"Left\" + 1 END",
            flag.GetComputedColumnSql());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, flag.ValueGenerated);
    }

    /// <summary>Incompatible property facets fail closed before a connection or migration can execute.</summary>
    /// <param name="facet">The incompatible application facet.</param>
    [Theory]
    [InlineData("type")]
    [InlineData("nullable")]
    [InlineData("converter")]
    [InlineData("concurrency")]
    [InlineData("computed")]
    [InlineData("stored")]
    [InlineData("default")]
    [InlineData("default-sql")]
    [InlineData("never-generated")]
    [InlineData("save")]
    [InlineData("after-save")]
    [InlineData("key")]
    [InlineData("foreign-key")]
    [InlineData("order")]
    public void ConflictingFlagMetadataIsRejected(
        string facet
    )
    {
        // Arrange
        var flagName = facet == "order" ? nameof(AncestorNode.NonLeaf) : "Flag";
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            if (facet == "type")
            {
                entity.Property<int>("Flag");
            }
            else if (facet == "nullable")
            {
                entity.Property<bool?>("Flag");
            }
            else
            {
                var property = entity.Property<bool>(flagName);
                switch (facet)
                {
                    case "converter":
                        property.HasConversion<int>();
                        break;
                    case "concurrency":
                        property.IsConcurrencyToken();
                        break;
                    case "computed":
                        property.HasComputedColumnSql("1", false);
                        break;
                    case "stored":
                        property.HasComputedColumnSql(
                            "\"Right\" > CASE WHEN \"Left\" = 9223372036854775807 THEN \"Left\""
                            + " ELSE \"Left\" + 1 END", true);
                        break;
                    case "default":
                        property.HasDefaultValue(false);
                        break;
                    case "default-sql":
                        property.HasDefaultValueSql("0");
                        break;
                    case "never-generated":
                        property.ValueGeneratedNever();
                        break;
                    case "save":
                        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
                        break;
                    case "after-save":
                        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Save);
                        break;
                    case "key":
                        entity.HasAlternateKey("Flag");
                        break;
                    case "foreign-key":
                        entity.HasOne<BooleanPrincipal>().WithMany().HasForeignKey("Flag");
                        break;
                    case "order":
                        entity.HasNestedSet(builder => builder.OrderBy(node => node.NonLeaf));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(facet));
                }
            }

            entity.HasNestedSet(builder => builder.HasAncestorIndex(flagName));
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains($"ancestor-index property '{flagName}'", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Invalid names fail inside the pending callback without adding metadata.</summary>
    /// <param name="name">The invalid optional generated property name.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyFlagNameDoesNotMutateModel(
        string name
    )
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = Configure(model);
        var original = entity.Metadata.GetProperties().ToArray();

        // Act
        var error = Record.Exception(() => entity.HasNestedSet(builder => builder.HasAncestorIndex(name)));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(error);
        Assert.Equal(original, entity.Metadata.GetProperties());
    }

    /// <summary>Removing an opted-in property cannot silently publish a model without its promised access path.</summary>
    [Fact]
    public void RemovedFlagIsRejectedAtFinalization()
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
        {
            var entity = Configure(model);
            entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
            entity.Ignore("Branch");
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("ancestor-index property 'Branch'", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("not mapped", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Implicit provider conversion selected by a store type cannot redefine Boolean predicate values.</summary>
    [Fact]
    public void ProviderSelectedBooleanConversionIsRejected()
    {
        // Arrange
        using var context = new AncestorContext("SqlServer", model =>
        {
            var entity = Configure(model);
            entity.Property<bool>("Branch").HasColumnType("varchar(1)");
            entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
        });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("provider-selected value converter", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Application-specialized ancestor indexes survive a later flag name change unchanged.</summary>
    /// <param name="facet">The explicit application index customization.</param>
    [Theory]
    [InlineData("filter")]
    [InlineData("descending")]
    [InlineData("unique")]
    [InlineData("annotation")]
    public void CustomizedAncestorIndexSurvivesFlagRemapping(
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
        var annotations = metadata.GetAnnotations().ToArray();
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
        Assert.Equal("NewFlag", entity.Metadata.FindAnnotation(NestedSetAnnotationNames.AncestorIndexProperty)?.Value);
    }

    /// <summary>Malformed imported optional metadata cannot silently downgrade to an unindexed hierarchy.</summary>
    /// <param name="value">The invalid present annotation value.</param>
    [Theory]
    [InlineData(17)]
    [InlineData("")]
    [InlineData(" ")]
    public void InvalidAncestorIndexAnnotationFailsClosed(
        object value
    )
    {
        // Arrange
        using var context = new AncestorContext("Sqlite", model =>
            Configure(model).Metadata.SetAnnotation(NestedSetAnnotationNames.AncestorIndexProperty, value));

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("invalid ancestor-index property metadata", invalid.Message, StringComparison.Ordinal);
    }
}
