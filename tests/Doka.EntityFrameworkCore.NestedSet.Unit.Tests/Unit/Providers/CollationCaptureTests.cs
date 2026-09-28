namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Distinguishes captured database-default equality from missing finalization metadata.</summary>
public sealed class CollationCaptureTests
{
    /// <summary>Verifies an unspecified string collation remains explicitly captured in the runtime model.</summary>
    [Fact]
    public async Task DatabaseDefaultCollationIsCapturedWithoutGuessing()
    {
        // Arrange
        await using var context = new DefaultCollationContext();
        var hierarchy = context.Model.FindEntityType(typeof(TextNode))!;
        var property = hierarchy.FindProperty(nameof(TextNode.Id))!;

        // Act
        var collation = NestedSetCollations.Resolve(context, property, hierarchy);

        // Assert
        Assert.Null(collation);
        Assert.Equal(
            string.Empty,
            hierarchy.FindAnnotation(NestedSetAnnotationNames.Collation + ":" + property.Name)!.Value);
        Assert.Equal(true, context.Model.FindAnnotation(NestedSetAnnotationNames.CollationsCaptured)!.Value);
    }

    /// <summary>Builds ordinary runtime metadata with no explicit property or model collation.</summary>
    private sealed class DefaultCollationContext : DbContext
    {
        /// <summary>Creates a connection-free SQLite model with the production convention pipeline.</summary>
        internal DefaultCollationContext() : base(
            new DbContextOptionsBuilder<DefaultCollationContext>()
                .ConfigureTestWarnings()
                .UseSqlite("Data Source=:memory:")
                .UseNestedSets()
                .Options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<TextNode>()
                .Property(row => row.Id)
                .ValueGeneratedNever();
            modelBuilder
                .Entity<TextNode>()
                .HasNestedSet(builder => builder
                    .HasTreeId(row => row.TreeId)
                    .HasScope(row => row.Tree)
                    .HasParent(row => row.ParentId));
        }
    }
}
