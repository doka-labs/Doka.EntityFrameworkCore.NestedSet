namespace Doka.EntityFrameworkCore.NestedSet.Migrations.Tests;

/// <summary>Protects the stable entity identity shared by historical and current migration models.</summary>
public sealed class MigrationContextTests
{
    /// <summary>Every model stage resolves its actual CLR shape through the same nonrecursive public identity.</summary>
    /// <param name="stage">The historical, current or specialized migration model.</param>
    [Theory]
    [InlineData(MigrationStage.Baseline)]
    [InlineData(MigrationStage.Current)]
    [InlineData(MigrationStage.Specialized)]
    public void StableEntityNameResolvesTheSelectedModel(
        MigrationStage stage
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<MigrationContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .ReplaceService<IModelCacheKeyFactory, MigrationModelCacheKeyFactory>()
            .Options;

        using var context = new MigrationContext(options, stage, null);
        var expectedType = stage == MigrationStage.Baseline
            ? typeof(BaselineMigrationNode)
            : typeof(MigrationNode);

        // Act
        var name = MigrationContext.EntityTypeName;
        var entity = context.Model.FindEntityType(name);

        // Assert
        Assert.Equal(typeof(MigrationNode).FullName, name);
        Assert.NotEqual(typeof(BaselineMigrationNode).FullName, name);
        Assert.NotNull(entity);
        Assert.Equal(expectedType, entity.ClrType);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
