namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Acquires complete tree identities in deterministic database order.</summary>
internal static class NestedSetTreeLocks
{
    /// <summary>Rejects input identities that the database considers equal under mapped store semantics.</summary>
    /// <param name="context">The context whose active transaction owns the validation snapshot.</param>
    /// <param name="requests">The complete tree identities supplied by one forest import.</param>
    /// <param name="cancellationToken">The token used for database-native comparison.</param>
    /// <returns>A task that completes when every supplied identity is distinct.</returns>
    internal static async Task RequireDistinctAsync(
        DbContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    )
    {
        var provider = NestedSetProviderCapabilities.Resolve(context);
        var groups = requests.GroupBy(request => request.Mapping.Registry);

        foreach (var group in groups)
        {
            var members = group.ToArray();
            var ordinals = await ResolveOrderAsync(context, members, provider, cancellationToken)
                .ConfigureAwait(false);

            if (ordinals.Count != members.Length)
            {
                throw new NestedSetException(
                    NestedSetErrorCode.InvalidImport,
                    "Every imported root must have a distinct TreeId within its Scope.");
            }
        }
    }

    /// <summary>Locks distinct requested trees without using CLR hashing or application comparison semantics.</summary>
    /// <param name="context">The context whose current transaction owns every acquired lock.</param>
    /// <param name="requests">The complete tree identities participating in one atomic operation.</param>
    /// <param name="cancellationToken">The token used for native ordering and lock acquisition.</param>
    /// <returns>A task that completes after every active or newly reserved registry row is locked.</returns>
    internal static async Task AcquireAsync(
        DbContext context,
        IReadOnlyList<INestedSetTreeLockRequest> requests,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (requests.Count == 0)
        {
            return;
        }

        var provider = NestedSetProviderCapabilities.Resolve(context);
        var groups = requests
            .GroupBy(request => request.Mapping.Registry)
            .OrderBy(group => group.Key.GetSchema(), StringComparer.Ordinal)
            .ThenBy(group => group.Key.GetTableName(), StringComparer.Ordinal)
            .ThenBy(group => group.Key.Name, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var members = group.ToArray();
            var ordinals = await ResolveOrderAsync(context, members, provider, cancellationToken)
                .ConfigureAwait(false);

            foreach (var ordinal in ordinals)
            {
                await new NestedSetTreeLock(context, provider, members[ordinal])
                    .AcquireAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Lets the database deduplicate and sort keys under their configured native semantics.</summary>
    /// <param name="context">The context supplying SQL and parameter type mappings.</param>
    /// <param name="requests">Requests that share one typed registry table.</param>
    /// <param name="provider">The verified dialect used to render collations and rowsets.</param>
    /// <param name="cancellationToken">The token used for the order query.</param>
    /// <returns>Representative request ordinals in canonical registry-key order.</returns>
    private static async Task<List<int>> ResolveOrderAsync(
        DbContext context,
        INestedSetTreeLockRequest[] requests,
        NestedSetProviderCapabilities provider,
        CancellationToken cancellationToken
    )
    {
        if (requests.Length == 1)
        {
            return [0];
        }

        var mapping = requests[0].Mapping;
        var sql = context.GetService<ISqlGenerationHelper>();
        var scopeAlias = sql.DelimitIdentifier(NestedSetTreeRegistryMetadata.Scope);
        var treeAlias = sql.DelimitIdentifier(NestedSetTreeRegistryMetadata.TreeId);
        var ordinalAlias = sql.DelimitIdentifier("Ordinal");
        var createAlias = sql.DelimitIdentifier("Create");
        var valueAlias = sql.DelimitIdentifier("Value");
        var sourceAlias = sql.DelimitIdentifier("RequestedTrees");
        var scopeExpression = mapping.Scope is null ? null : "r." + scopeAlias;
        var treeExpression = "r." + treeAlias;

        if (mapping.SourceScope is { } scope
            && HasStringStore(scope))
        {
            scopeExpression = ApplyCollation(context, provider, sql, scopeExpression!, scope, mapping.Hierarchy);
        }

        if (HasStringStore(mapping.SourceTreeId))
        {
            treeExpression = ApplyCollation(
                context,
                provider,
                sql,
                treeExpression,
                mapping.SourceTreeId,
                mapping.Hierarchy);
        }

        await using var command = context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var parameters = new List<object>(provider.TreeRowsetParameterCount(requests.Length, mapping.Scope is not null));
        var aliases = new NestedSetTreeRowsetAliases(sourceAlias, scopeAlias, treeAlias, ordinalAlias, createAlias);
        var sourceStatement = provider.BuildTreeRowsetSource(command, parameters, aliases, mapping, requests);
        var identity = scopeExpression is null ? treeExpression : scopeExpression + ", " + treeExpression;

        // WHY: Create participates in grouping so a mixed create/existing request never loses its stricter
        // reservation contract. Descending order tries that reservation before an existing-row lock.
        var statement = sourceStatement
            + $"SELECT MIN(r.{ordinalAlias}) AS {valueAlias} FROM {sourceAlias} AS r "
            + $"GROUP BY {identity}, r.{createAlias} "
            + $"ORDER BY {identity}, r.{createAlias} DESC";

        return await context
            .Database
            .SqlQueryRaw<int>(statement, parameters.ToArray())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Applies the captured effective string collation to derived parameter values.</summary>
    /// <param name="context">The context owning the finalized hierarchy metadata.</param>
    /// <param name="provider">The verified provider whose collation syntax is used.</param>
    /// <param name="sql">The provider's SQL identifier service.</param>
    /// <param name="expression">The native request identity expression.</param>
    /// <param name="source">The source hierarchy identity property.</param>
    /// <param name="hierarchy">The configured owner, including a concrete inherited-property mapping.</param>
    /// <returns>The known source collation expression, or the original expression for an unknown default.</returns>
    private static string ApplyCollation(
        DbContext context,
        NestedSetProviderCapabilities provider,
        ISqlGenerationHelper sql,
        string expression,
        IProperty source,
        IEntityType hierarchy
    )
    {
        var collation = NestedSetCollations.Resolve(context, source, hierarchy);

        return collation is null ? expression : expression + " COLLATE " + provider.CollationSql(sql, collation);
    }

    /// <summary>Checks the finalized provider type used by a registry identity column.</summary>
    private static bool HasStringStore(
        IProperty property
    ) => (property.GetRelationalTypeMapping().Converter?.ProviderClrType ?? property.ClrType) == typeof(string);
}
