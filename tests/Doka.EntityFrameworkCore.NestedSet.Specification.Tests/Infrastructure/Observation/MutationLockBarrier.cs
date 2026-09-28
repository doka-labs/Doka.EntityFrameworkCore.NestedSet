namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Holds both server writers at their exact-tree locking query before either acquires its registry row.
/// </summary>
internal sealed class MutationLockBarrier : DbCommandInterceptor
{
    private readonly NestedSetTreeRegistryMapping _mapping;
    private readonly int _scope;
    private readonly Guid _treeId;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    /// <summary>Identifies the complete indexed registry key shared by the competing operations.</summary>
    /// <param name="hierarchy">The finalized hierarchy metadata owning the physical registry.</param>
    /// <param name="scope">The application partition expected in both lock commands.</param>
    /// <param name="treeId">The stable tree identity expected in both lock commands.</param>
    internal MutationLockBarrier(
        IEntityType hierarchy,
        int scope,
        Guid treeId
    )
    {
        _mapping = NestedSetTreeRegistryMapping.For(hierarchy);
        _scope = scope;
        _treeId = treeId;
    }

    /// <summary>Gets the number of verified exact-tree infrastructure lock attempts.</summary>
    internal int Arrivals => Volatile.Read(ref _arrivals);

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        var context = eventData.Context;

        if (context is not null)
        {
            var sql = context.GetService<ISqlGenerationHelper>();
            var table = sql.DelimitIdentifier(_mapping.Store.Name, _mapping.Store.Schema);

            if (command.CommandText.Contains(table, StringComparison.Ordinal))
            {
                // WHY: Existing TreeIds need no reservation INSERT. Server engines acquire their row lock
                // through this lifecycle SELECT, so both writers must rendezvous before it reaches the database.
                RequireIndexedIdentity(command, sql);
                await WaitForBothAsync(cancellationToken);
            }
        }

        return result;
    }

    /// <summary>
    ///     Verifies a locking query targets the complete primary key and its exact provider-mapped values.
    /// </summary>
    private void RequireIndexedIdentity(
        DbCommand command,
        ISqlGenerationHelper sql
    )
    {
        var scope = _mapping.Scope;
        var primaryKey = _mapping.Registry.FindPrimaryKey();
        Assert.NotNull(scope);
        Assert.NotNull(primaryKey);

        Assert.Collection(
            primaryKey.Properties,
            property => Assert.Same(scope, property),
            property => Assert.Same(_mapping.TreeId, property));

        var lockingQuery = command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
            || command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.OrdinalIgnoreCase)
            || (command.CommandText.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("HOLDLOCK", StringComparison.OrdinalIgnoreCase));

        Assert.True(lockingQuery, "Expected a server row-lock query: " + command.CommandText);
        var scopeColumn = sql.DelimitIdentifier(scope.GetColumnName(_mapping.Store)!);
        var treeIdColumn = sql.DelimitIdentifier(_mapping.TreeId.GetColumnName(_mapping.Store)!);

        Assert.Contains(scopeColumn + " = " + sql.GenerateParameterName("scope"), command.CommandText);
        Assert.Contains(treeIdColumn + " = " + sql.GenerateParameterName("treeId"), command.CommandText);

        var connection = command.Connection;
        Assert.NotNull(connection);

        using var expected = connection.CreateCommand();
        var expectedScope = scope
            .GetRelationalTypeMapping()
            .CreateParameter(expected, "scope", _scope, nullable: false);

        var expectedTreeId = _mapping
            .TreeId
            .GetRelationalTypeMapping()
            .CreateParameter(expected, "treeId", _treeId, nullable: false);

        // WHY: Doka maps Guid parameters to binary storage. Compare the values produced by its actual mapping
        // instead of assuming a text Guid or coupling this concurrency probe to a provider representation.
        Assert.Collection(
            command.Parameters.Cast<DbParameter>(),
            parameter => Assert.Equal(expectedScope.Value, parameter.Value),
            parameter => Assert.Equal(expectedTreeId.Value, parameter.Value));
    }

    /// <summary>Releases both observed writers only after both reached their provider's lock statement.</summary>
    private async Task WaitForBothAsync(
        CancellationToken cancellationToken
    )
    {
        if (Interlocked.Increment(ref _arrivals) == 2)
        {
            _ready.TrySetResult();
        }

        await _ready.Task.WaitAsync(cancellationToken);
    }
}
