namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Holds successful exact-tree registry lock commands before a mutation can reach structural writes.</summary>
internal sealed class ConcurrentCapacityLockProbe : DbCommandInterceptor
{
    private readonly int _scope;
    private readonly Guid _treeId;
    private readonly ConcurrentCapacityBarrier _barrier;

    /// <summary>Observes the complete registry identity assigned exclusively to this writer.</summary>
    internal ConcurrentCapacityLockProbe(
        int scope,
        Guid treeId,
        ConcurrentCapacityBarrier barrier
    )
    {
        _scope = scope;
        _treeId = treeId;
        _barrier = barrier;
    }

    /// <summary>Gets the number of registry queries whose first locked row was read successfully.</summary>
    internal int AcquiredLocks { get; private set; }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        var context = eventData.Context;

        if (context is null
            || !NestedSetTestInfrastructure.ReferencesRegistry(context, command.CommandText))
        {
            return ValueTask.FromResult(result);
        }

        var lockingQuery = command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
            || command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.OrdinalIgnoreCase)
            || (command.CommandText.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("HOLDLOCK", StringComparison.OrdinalIgnoreCase));

        if (!lockingQuery)
        {
            return ValueTask.FromResult(result);
        }

        RequireExactIdentity(context, command);

        // WHY: SQL Server can deliver reader metadata before the locked row. Pause after the successful
        // first row read so all 64 transactions demonstrably hold their locks before structural writes.
        return ValueTask.FromResult<DbDataReader>(
            new ConcurrentCapacityLockReader(result, () => AcquiredLocks++, _barrier.ArriveAsync));
    }

    /// <summary>Checks the complete mapped primary key and provider-native values of the locking query.</summary>
    private void RequireExactIdentity(
        DbContext context,
        DbCommand command
    )
    {
        var hierarchy = context.Model.FindEntityType(typeof(ConcurrentNode))!;
        var mapping = NestedSetTreeRegistryMapping.For(hierarchy);
        var scope = mapping.Scope;
        var primaryKey = mapping.Registry.FindPrimaryKey();
        Assert.NotNull(scope);
        Assert.NotNull(primaryKey);
        Assert.Collection(
            primaryKey.Properties,
            property => Assert.Same(scope, property),
            property => Assert.Same(mapping.TreeId, property));

        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(mapping.Store.Name, mapping.Store.Schema);
        var scopeColumn = sql.DelimitIdentifier(scope.GetColumnName(mapping.Store)!);
        var treeIdColumn = sql.DelimitIdentifier(mapping.TreeId.GetColumnName(mapping.Store)!);
        Assert.Contains(table, command.CommandText);
        Assert.Contains(scopeColumn + " = " + sql.GenerateParameterName("scope"), command.CommandText);
        Assert.Contains(treeIdColumn + " = " + sql.GenerateParameterName("treeId"), command.CommandText);
        Assert.NotNull(command.Connection);

        using var expected = command.Connection.CreateCommand();
        var expectedScope = scope
            .GetRelationalTypeMapping()
            .CreateParameter(expected, "scope", _scope, nullable: false);

        var expectedTreeId = mapping
            .TreeId
            .GetRelationalTypeMapping()
            .CreateParameter(expected, "treeId", _treeId, nullable: false);

        // WHY: The Guid representation differs between providers; compare values from the actual model mapping.
        Assert.Collection(
            command.Parameters.Cast<DbParameter>(),
            parameter => Assert.Equal(expectedScope.Value, parameter.Value),
            parameter => Assert.Equal(expectedTreeId.Value, parameter.Value));
    }
}
