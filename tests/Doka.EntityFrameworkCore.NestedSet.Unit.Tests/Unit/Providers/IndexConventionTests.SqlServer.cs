namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>Verifies ordinary SQL Server physical options preserve the existing complete access path.</summary>
    [Theory]
    [InlineData("include")]
    [InlineData("clustered")]
    [InlineData("online")]
    [InlineData("fillfactor")]
    [InlineData("sort")]
    public void SqlServerPhysicalIndexOptionsDoNotCreateDuplicateIndexes(
        string option
    )
    {
        // Arrange
        using var context = new ConventionContext(
            "SqlServer",
            model =>
            {
                var entity = model.Entity<IndexNode>();
                entity.HasKey(node => node.Key);
                var index = entity.HasIndex(
                    node => new
                    {
                        node.Scope,
                        node.TreeId,
                        node.Left,
                    },
                    "CustomLeft");

                switch (option)
                {
                    case "include":
                        SqlServerIndexBuilderExtensions.IncludeProperties(index, node => node.OtherKey);
                        break;
                    case "clustered":
                        index.IsClustered(false);
                        break;
                    case "online":
                        index.IsCreatedOnline();
                        break;
                    case "fillfactor":
                        index.HasFillFactor(80);
                        break;
                    case "sort":
                        index.SortInTempDb();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(option));
                }

                Configure(entity);
            });

        // Act
        var model = context.GetService<IDesignTimeModel>()
            .Model;

        // Assert
        var indexes = model.FindEntityType(typeof(IndexNode))!
            .GetIndexes()
            .ToArray();
        Assert.Equal(4, indexes.Length);
        Assert.Single(
            indexes,
            index => index
                .Properties
                .Select(property => property.Name)
                .SequenceEqual(new[] { nameof(IndexNode.Scope), nameof(IndexNode.TreeId), nameof(IndexNode.Left), }));
        Assert.Contains(
            indexes,
            index => index
                .Properties
                .Select(property => property.Name)
                .SequenceEqual(new[] { nameof(IndexNode.Scope), nameof(IndexNode.Parent) }));
    }
}
