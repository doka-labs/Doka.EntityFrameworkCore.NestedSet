namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Advances or retires an exact tree registry row while its transaction owns the row lock.</summary>
internal static class NestedSetTreeRegistryState
{
    /// <summary>Advances the structural revision of one active tree.</summary>
    /// <param name="context">The context whose current transaction owns the registry lock.</param>
    /// <param name="request">The exact optional Scope and mandatory TreeId identity.</param>
    /// <param name="cancellationToken">The token used for the database update.</param>
    /// <returns>A task that completes after exactly one active row was updated.</returns>
    internal static Task TouchAsync(
        DbContext context,
        INestedSetTreeLockRequest request,
        CancellationToken cancellationToken
    ) => UpdateAsync(context, request, tombstone: false, cancellationToken);

    /// <summary>Reserves a deleted TreeId permanently by changing its active row to a tombstone.</summary>
    /// <param name="context">The context whose current transaction owns the registry lock.</param>
    /// <param name="request">The exact optional Scope and mandatory TreeId identity.</param>
    /// <param name="cancellationToken">The token used for the database update.</param>
    /// <returns>A task that completes after exactly one active row was retired.</returns>
    internal static Task TombstoneAsync(
        DbContext context,
        INestedSetTreeLockRequest request,
        CancellationToken cancellationToken
    ) => UpdateAsync(context, request, tombstone: true, cancellationToken);

    /// <summary>Removes one locked tombstone so an operator may deliberately reuse its TreeId.</summary>
    /// <param name="context">The context whose current transaction owns the tombstone row lock.</param>
    /// <param name="request">The exact optional Scope and mandatory TreeId identity.</param>
    /// <param name="cancellationToken">The token used for the database delete.</param>
    /// <returns>A task that completes after exactly one tombstone was removed.</returns>
    internal static async Task PurgeAsync(
        DbContext context,
        INestedSetTreeLockRequest request,
        CancellationToken cancellationToken
    )
    {
        var mapping = request.Mapping;
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(mapping.Store.Name, mapping.Store.Schema);
        var treeId = sql.DelimitIdentifier(mapping.TreeId.GetColumnName(mapping.Store)!);
        var lifecycle = sql.DelimitIdentifier(mapping.Lifecycle.GetColumnName(mapping.Store)!);
        var scope = mapping.Scope is null
            ? null
            : sql.DelimitIdentifier(mapping.Scope.GetColumnName(mapping.Store)!);

        var predicate = scope is null
            ? $"{treeId} = {{0}}"
            : $"{scope} = {{0}} AND {treeId} = {{1}}";

        var statement = $"DELETE FROM {table} WHERE {predicate} AND {lifecycle} = {NestedSetTreeRegistryMetadata.Tombstoned}";

        var affected = await context
            .Database
            .ExecuteSqlRawAsync(statement, CreateParameters(context, request), cancellationToken)
            .ConfigureAwait(false);

        if (affected != 1)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                "The locked tree registry row did not have the expected tombstoned lifecycle.");
        }
    }

    /// <summary>Executes one provider-portable registry update with model-derived parameter mappings.</summary>
    private static async Task UpdateAsync(
        DbContext context,
        INestedSetTreeLockRequest request,
        bool tombstone,
        CancellationToken cancellationToken
    )
    {
        var mapping = request.Mapping;
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(mapping.Store.Name, mapping.Store.Schema);
        var treeId = sql.DelimitIdentifier(mapping.TreeId.GetColumnName(mapping.Store)!);
        var revision = sql.DelimitIdentifier(mapping.Revision.GetColumnName(mapping.Store)!);
        var lifecycle = sql.DelimitIdentifier(mapping.Lifecycle.GetColumnName(mapping.Store)!);
        var scope = mapping.Scope is null ? null : sql.DelimitIdentifier(mapping.Scope.GetColumnName(mapping.Store)!);

        var predicate = scope is null
            ? $"{treeId} = {{0}}"
            : $"{scope} = {{0}} AND {treeId} = {{1}}";

        var assignment = tombstone
            ? $"{revision} = {revision} + 1, {lifecycle} = {NestedSetTreeRegistryMetadata.Tombstoned}"
            : $"{revision} = {revision} + 1";

        var statement = $"UPDATE {table} SET {assignment} WHERE {predicate} AND {lifecycle} = {NestedSetTreeRegistryMetadata.Active}";

        var affected = await context
            .Database
            .ExecuteSqlRawAsync(statement, CreateParameters(context, request), cancellationToken)
            .ConfigureAwait(false);

        if (affected != 1)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                "The locked tree registry row did not have the expected active lifecycle.");
        }
    }

    /// <summary>Creates exact relational parameters for one registry identity.</summary>
    private static object[] CreateParameters(
        DbContext context,
        INestedSetTreeLockRequest request
    )
    {
        var mapping = request.Mapping;
        using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        if (mapping.Scope is null)
        {
            return
            [
                mapping
                    .TreeId
                    .GetRelationalTypeMapping()
                    .CreateParameter(command, "treeId", request.TreeIdValue, nullable: false),
            ];
        }

        return
        [
            mapping
                .Scope
                .GetRelationalTypeMapping()
                .CreateParameter(command, "scope", request.ScopeValue, nullable: false),
            mapping
                .TreeId
                .GetRelationalTypeMapping()
                .CreateParameter(command, "treeId", request.TreeIdValue, nullable: false),
        ];
    }
}
