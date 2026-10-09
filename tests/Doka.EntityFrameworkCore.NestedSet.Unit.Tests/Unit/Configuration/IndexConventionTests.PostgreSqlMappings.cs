using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class IndexConventionTests
{
    /// <summary>Root indexes map only to the physical table containing their Parent predicate column.</summary>
    /// <param name="mapping">The supported relational mapping strategy.</param>
    /// <param name="table">The physical table containing every hierarchy property.</param>
    [Theory]
    [InlineData("Tph", "TphNodes")]
    [InlineData("Tpt", "TptNodes")]
    [InlineData("TableSplit", "TableSplitNodes")]
    [InlineData("EntitySplit", "EntitySplitStructure")]
    public void PostgreSqlRootIndexesStayInStructuralTable(
        string mapping,
        string table
    )
    {
        // Arrange
        using DbContext context = mapping switch
        {
            "Tph" => new TphContext(ModelCompatibilityDatabase.Options<TphContext>("PostgreSql")),
            "Tpt" => new TptContext(ModelCompatibilityDatabase.Options<TptContext>("PostgreSql")),
            "TableSplit" => new TableSplitContext(ModelCompatibilityDatabase.Options<TableSplitContext>("PostgreSql")),
            "EntitySplit" => new EntitySplitContext(
                ModelCompatibilityDatabase.Options<EntitySplitContext>("PostgreSql")),
            _ => throw new ArgumentOutOfRangeException(nameof(mapping)),
        };

        var model = context
            .GetService<IDesignTimeModel>()
            .Model
            .GetRelationalModel();

        // Act
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model);

        // Assert
        var roots = model
            .Tables
            .SelectMany(candidate => candidate.Indexes)
            .Where(index => index.Name.StartsWith("IX_NestedSet_Roots_", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(mapping == "EntitySplit" ? 0 : 1, roots.Length);
        Assert.All(
            roots,
            index =>
            {
                Assert.Equal(table, index.Table.Name);
                Assert.Equal(["Id", "ParentId"], index.Columns.Select(column => column.Name));
                Assert.Equal("\"ParentId\" IS NULL", index.Filter);

                var create = Assert.Single(
                    operations.OfType<CreateIndexOperation>(),
                    operation => operation.Name == index.Name);

                Assert.Equal(table, create.Table);
                Assert.Equal(["Id", "ParentId"], create.Columns);
                Assert.Equal(index.Filter, create.Filter);
            });

        Assert.All(
            model
                .Tables
                .SelectMany(candidate => candidate.Indexes)
                .Where(index => index.Filter?.Contains("ParentId", StringComparison.Ordinal) == true),
            index => Assert.Contains(index.Table.Columns, column => column.Name == "ParentId"));
    }
}
