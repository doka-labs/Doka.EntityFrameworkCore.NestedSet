using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class AncestorIndexTests
{
    /// <summary>Application-qualified bounded identifiers generate a complete access path within InnoDB's limit.</summary>
    /// <param name="engine">The configured InnoDB engine family.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void ExplicitStringWidthsRemainWithinProviderIndexBudget(
        string engine
    )
    {
        // Arrange
        using var context = new AncestorContext(engine, model =>
        {
            var entity = model.SharedTypeEntity<Dictionary<string, object>>("StringIndexBudget");
            entity.Property<string>("Id").IsRequired().HasMaxLength(64);
            entity.Property<string>("Scope").IsRequired().HasMaxLength(64);
            entity.Property<string>("Tree").IsRequired().HasMaxLength(64);
            entity.Property<string>("Parent").HasMaxLength(64);
            entity.Property<long>("L");
            entity.Property<long>("R");
            entity.Property<int>("Depth");
            entity.Property<long>("Position");
            entity.HasKey("Id");
            entity.HasNestedSet(builder => builder
                .HasScope("Scope")
                .HasTreeId("Tree")
                .HasParent("Parent")
                .HasBounds("L", "R")
                .HasDepth("Depth")
                .HasPosition("Position")
                .HasAncestorIndex());
        });

        // Act
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType("StringIndexBudget")!;
        var flag = entity.FindProperty("NestedSetIsNonLeaf")!;
        var index = entity.GetIndexes().Single(index => index.Properties.Contains(flag));
        var maximumBytes = index.Properties.Sum(property => property.ClrType == typeof(string)
            ? 4 * property.GetMaxLength()!.Value
            : property.ClrType == typeof(long) ? 8 : 1);

        // Assert
        Assert.InRange(maximumBytes, 1, 3072);
        Assert.Contains("GENERATED ALWAYS AS", context.Database.GenerateCreateScript(), StringComparison.Ordinal);
    }

    /// <summary>NestedSet declares its complete optional index before the provider's key-width finalizer executes.</summary>
    [Fact]
    public void AncestorDeclarationPrecedesProviderIndexBudgeting()
    {
        // Arrange
        using var context = new AncestorContext("MySql", model => Configure(model));

        // Act
        var conventions = context.GetService<IConventionSetBuilder>().CreateConventionSet()
            .ModelFinalizingConventions.ToArray();

        var declaration = Array.FindIndex(conventions, convention => convention is NestedSetAncestorIndexConvention);
        var budgeting = Array.FindIndex(conventions, convention => convention.GetType().Name == "MySqlIndexLengthConvention");

        // Assert
        Assert.InRange(declaration, 0, budgeting - 1);
    }
}
