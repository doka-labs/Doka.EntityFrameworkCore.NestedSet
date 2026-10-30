using System.Data.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ProviderCapabilitiesTests
{
    /// <summary>Scalar transports reserve exact identity capacity and switch to one parameter at their boundary.</summary>
    /// <param name="engine">The selected native adapter.</param>
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    public void ScalarAndCollectionParameterCountsPreserveScopeAndBoundary(
        string engine
    )
    {
        // Arrange
        var provider = NestedSetTestAdapters.ForEngine(engine);

        // Act
        int[] counts =
        [
            provider.TreeRowsetParameterCount(1, scoped: false),
            provider.TreeRowsetParameterCount(1, scoped: true),
            provider.TreeRowsetParameterCount(255, scoped: false),
            provider.TreeRowsetParameterCount(255, scoped: true),
            provider.TreeRowsetParameterCount(256, scoped: false),
            provider.TreeRowsetParameterCount(256, scoped: true),
            provider.TreeRowsetParameterCount(257, scoped: false),
            provider.TreeRowsetParameterCount(257, scoped: true),
        ];

        // Assert
        Assert.Equal([1, 2, 255, 510, 1, 1, 1, 1], counts);
    }

    /// <summary>SQL Server retains one JSON parameter even below the scalar threshold used by other adapters.</summary>
    /// <param name="requestCount">The identity count below, at, or above the shared scalar threshold.</param>
    /// <param name="scoped">Whether each requested identity includes a scope value.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(255, false)]
    [InlineData(255, true)]
    [InlineData(256, false)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    [InlineData(257, true)]
    public void SqlServerParameterCountPreservesItsJsonOnlyTransport(
        int requestCount,
        bool scoped
    )
    {
        // Arrange
        var provider = NestedSetTestAdapters.ForEngine("SqlServer");

        // Act
        var count = provider.TreeRowsetParameterCount(requestCount, scoped);

        // Assert
        Assert.Equal(1, count);
    }

    /// <summary>An unscoped scalar source cannot introduce a scope column or discard lifecycle ordinals.</summary>
    /// <param name="engine">The native adapter owning the source's WITH/VALUES syntax.</param>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    public void UnscopedScalarRowsPreserveMappedValuesAndLifecycleWithoutScope(
        string engine
    )
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
        var metadata = context.Model.FindEntityType(CompositionGuardContext.FirstMapping)!;
        var treeId = new Guid("00000000-0000-0000-0000-000000000001");
        INestedSetTreeLockRequest[] requests =
        [
            new NestedSetTreeLockRequest<Guid, NestedSetNoScope>(
                metadata, default, Guid.Empty, NestedSetTreeLockMode.New),
            new NestedSetTreeLockRequest<Guid, NestedSetNoScope>(
                metadata, default, treeId, NestedSetTreeLockMode.Tombstoned),
        ];

        var aliases = new NestedSetTreeRowsetAliases(
            "\"RequestedTrees\"", "\"Scope\"", "\"TreeId\"", "\"Ordinal\"", "\"Create\"");

        using var command = context.Database.GetDbConnection().CreateCommand();
        var parameters = new List<object>();
        var adapter = NestedSetTestAdapters.ForEngine(engine);

        // Act
        var sql = adapter.BuildTreeRowsetSource(command, parameters, aliases, requests[0].Mapping, requests);

        // Assert
        Assert.Equal("WITH \"RequestedTrees\" (\"TreeId\", \"Ordinal\", \"Create\") AS (VALUES "
            + "({0}, 0, 1), ({1}, 1, 2)) ", sql);
        Assert.Equal(2, parameters.Count);
        Assert.Equal(["treeId0", "treeId1"], parameters.Cast<DbParameter>()
            .Select(parameter => parameter.ParameterName));
        Assert.Equal(Guid.Empty, ((DbParameter)parameters[0]).Value);
        Assert.Equal(treeId, ((DbParameter)parameters[1]).Value);
        Assert.Equal(0, boundary.ConnectionStarts);
        Assert.Equal(0, boundary.Commands.Created);
    }
}
