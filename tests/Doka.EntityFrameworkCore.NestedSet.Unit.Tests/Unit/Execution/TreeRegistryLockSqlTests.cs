using System.Data.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies actual adapter-owned typed tree-registry SQL without database execution.</summary>
public sealed class TreeRegistryLockSqlTests
{
    /// <summary>PostgreSQL and SQLite own direct scalar CTE sources with exact mapped request ordinals.</summary>
    /// <param name="engine">The actual adapter whose native relational mappings build the parameters.</param>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    public void RelationalValuesSourceUsesDirectCommonTableExpression(string engine)
    {
        // Arrange
        var connection = engine == "Sqlite"
            ? "Data Source=:memory:"
            : "Host=127.0.0.1;Database=rowset_contract;Username=unused;Pooling=false";

        var boundary = new CompositionIoBoundary();
        var options = new DbContextOptionsBuilder<CompositionGuardContext>(
                ModelCompatibilityDatabase.Options<CompositionGuardContext>(engine, connection))
            .AddInterceptors(boundary, boundary.Commands).Options;

        using var owner = OwnedModelTestServices<CompositionGuardContext>.Create(options);
        using var context = new CompositionGuardContext(owner.Options);
        var metadata = context.Model.FindEntityType(CompositionGuardContext.ScopedMapping)!;
        INestedSetTreeLockRequest[] requests =
        [
            new NestedSetTreeLockRequest<Guid, int>(metadata, 1, Guid.Empty, NestedSetTreeLockMode.New),
            new NestedSetTreeLockRequest<Guid, int>(metadata, 2, new Guid("00000000-0000-0000-0000-000000000001"),
                NestedSetTreeLockMode.Existing),
        ];

        var aliases = new NestedSetTreeRowsetAliases("\"RequestedTrees\"", "\"Scope\"", "\"TreeId\"",
            "\"Ordinal\"", "\"Create\"");

        using var command = context.Database.GetDbConnection().CreateCommand();
        var parameters = new List<object>();
        var adapter = NestedSetTestAdapters.ForEngine(engine);

        // Act
        var sql = adapter.BuildTreeRowsetSource(command, parameters, aliases, requests[0].Mapping, requests);

        // Assert
        Assert.Equal("WITH \"RequestedTrees\" (\"Scope\", \"TreeId\", \"Ordinal\", \"Create\") AS (VALUES "
            + "({0}, {1}, 0, 1), ({2}, {3}, 1, 0)) ", sql);
        Assert.Equal(adapter.TreeRowsetParameterCount(requests.Length, scoped: true), parameters.Count);
        Assert.Equal(["scope0", "treeId0", "scope1", "treeId1"], parameters.Cast<DbParameter>()
            .Select(parameter => parameter.ParameterName));
        Assert.Equal(1, ((DbParameter)parameters[0]).Value);
        Assert.Equal(2, ((DbParameter)parameters[2]).Value);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }
}
