namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
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
    public void FinalizeModel_ConflictingFlagMetadata_IsRejected(
        string facet
    )
    {
        // Arrange
        var flagName = facet == "order"
            ? nameof(AncestorNode.NonLeaf)
            : "Flag";

        using var context = new AncestorContext(
            "Sqlite",
            model =>
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
                                + " ELSE \"Left\" + 1 END",
                                true);

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
                            entity
                                .HasOne<BooleanPrincipal>()
                                .WithMany()
                                .HasForeignKey("Flag");

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
    public void HasNestedSet_EmptyFlagName_DoesNotMutateModel(
        string name
    )
    {
        // Arrange
        var model = new ModelBuilder();
        var entity = Configure(model);
        var original = entity
            .Metadata
            .GetProperties()
            .ToArray();

        // Act
        var error = Record.Exception(() => entity.HasNestedSet(builder => builder.HasAncestorIndex(name)));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(error);
        Assert.Equal(original, entity.Metadata.GetProperties());
    }

    /// <summary>
    /// Removing an opted-in property cannot silently publish a model without its promised access path.
    /// </summary>
    [Fact]
    public void FinalizeModel_RemovedFlag_IsRejectedAtFinalization()
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
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

    /// <summary>
    /// Implicit provider conversion selected by a store type cannot redefine Boolean predicate values.
    /// </summary>
    [Fact]
    public void FinalizeModel_ProviderSelectedBooleanConversion_IsRejected()
    {
        // Arrange
        using var context = new AncestorContext(
            "SqlServer",
            model =>
            {
                var entity = Configure(model);
                entity
                    .Property<bool>("Branch")
                    .HasColumnType("varchar(1)");

                entity.HasNestedSet(builder => builder.HasAncestorIndex("Branch"));
            });

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("provider-selected value converter", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>Malformed imported optional metadata cannot silently downgrade to an unindexed hierarchy.</summary>
    /// <param name="value">The invalid present annotation value.</param>
    [Theory]
    [InlineData(17)]
    [InlineData("")]
    [InlineData(" ")]
    public void FinalizeModel_InvalidAncestorIndexAnnotation_FailsClosed(
        object value
    )
    {
        // Arrange
        using var context = new AncestorContext(
            "Sqlite",
            model =>
                Configure(model)
                    .Metadata
                    .SetAnnotation(NestedSetAnnotationNames.AncestorIndexProperty, value));

        // Act
        var error = Record.Exception(() => _ = context.Model);

        // Assert
        var invalid = Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.Contains("invalid ancestor-index property metadata", invalid.Message, StringComparison.Ordinal);
    }
}
