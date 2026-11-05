using MappingNode = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeNode;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Distinguishes supported Parent relationships from implicit unique structural constraints.</summary>
public sealed class ParentRelationshipMappingTests
{
    /// <summary>The ordinary EF one-to-one convention remains subject to the structural-index prohibition.</summary>
    /// <param name="unique">Whether EF convention creates a unique Parent foreign-key index.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentRelationshipRespectsStructuralIndexContract(
        bool unique
    )
    {
        // Arrange
        await using var context = new ParentMappingContext(unique);
        var metadata = context.Model.FindEntityType(typeof(MappingNode))!;

        // Act
        var error = Record.Exception(() => NestedSetMapping<MappingNode, int, int>.For(context, metadata));

        // Assert
        var foreignKey = Assert.Single(metadata.GetForeignKeys());
        Assert.Equal(unique, foreignKey.IsUnique);
        Assert.Equal(
            [nameof(MappingNode.Scope), nameof(MappingNode.ParentId)],
            foreignKey.Properties.Select(property => property.Name));

        if (unique)
        {
            var rejection = Assert.IsType<InvalidOperationException>(error);
            Assert.Equal("Unique nested-set structural indexes and keys are unsupported.", rejection.Message);
        }
        else
        {
            Assert.Null(error);
        }

        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Uses EF's standard relationship convention with the same existing synthetic node model.</summary>
    private sealed class ParentMappingContext(
        bool unique
    ) : DbContext, ITestModelVariant
    {
        /// <inheritdoc />
        object ITestModelVariant.ModelVariant => unique;

        /// <inheritdoc />
        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder
        ) => optionsBuilder
            .ConfigureTestWarnings()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .ReplaceService<IModelCacheKeyFactory, TestModelCacheKeyFactory>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var node = modelBuilder.Entity<MappingNode>();
            node
                .Property(value => value.Id)
                .ValueGeneratedNever();

            node.Ignore(value => value.Children);
            node.Ignore(value => value.Owner);
            node.Ignore(value => value.ScopeOwner);
            node.HasNestedSet(value => value
                .HasScope(item => item.Scope)
                .HasTreeId(item => item.TreeId)
                .HasParent(item => item.ParentId)
                .OrderBy(item => item.Name));

            if (unique)
            {
                // WHY: This is the normal one-to-one FK convention, without manually creating a unique index.
                node
                    .HasOne(value => value.Parent)
                    .WithOne()
                    .HasForeignKey<MappingNode>(value => new
                    {
                        value.Scope,
                        value.ParentId,
                    })
                    .HasPrincipalKey<MappingNode>(value => new
                    {
                        value.Scope,
                        value.Id,
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                node
                    .HasOne(value => value.Parent)
                    .WithMany()
                    .HasForeignKey(value => new
                    {
                        value.Scope,
                        value.ParentId,
                    })
                    .HasPrincipalKey(value => new
                    {
                        value.Scope,
                        value.Id,
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            }
        }
    }
}
