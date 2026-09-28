namespace Doka.EntityFrameworkCore.NestedSet.Execution;

/// <summary>Atomically reserves or locks one typed tree-registry row.</summary>
internal sealed class NestedSetTreeLock
{
    private readonly DbContext _context;
    private readonly NestedSetProviderCapabilities _provider;
    private readonly INestedSetTreeLockRequest _request;
    private readonly string _table;
    private readonly string? _scope;
    private readonly string _treeId;
    private readonly string _revision;
    private readonly string _lifecycle;

    /// <summary>Builds provider SQL from finalized relational model metadata.</summary>
    /// <param name="context">The context whose current transaction owns the lock.</param>
    /// <param name="provider">The verified provider dialect.</param>
    /// <param name="request">The complete tree identity and lifecycle requirement.</param>
    internal NestedSetTreeLock(
        DbContext context,
        NestedSetProviderCapabilities provider,
        INestedSetTreeLockRequest request
    )
    {
        _context = context;
        _provider = provider;
        _request = request;

        var mapping = request.Mapping;
        var sql = context.GetService<ISqlGenerationHelper>();
        _table = sql.DelimitIdentifier(mapping.Store.Name, mapping.Store.Schema);
        _scope = mapping.Scope is null ? null : sql.DelimitIdentifier(mapping.Scope.GetColumnName(mapping.Store)!);

        _treeId = sql.DelimitIdentifier(mapping.TreeId.GetColumnName(mapping.Store)!);
        _revision = sql.DelimitIdentifier(mapping.Revision.GetColumnName(mapping.Store)!);
        _lifecycle = sql.DelimitIdentifier(mapping.Lifecycle.GetColumnName(mapping.Store)!);
    }

    /// <summary>Acquires one row lock and enforces active, unused, and tombstoned lifecycle rules.</summary>
    /// <param name="cancellationToken">The token used while waiting for and acquiring the database lock.</param>
    /// <returns>A task that completes after the transaction owns the registry row lock.</returns>
    internal Task AcquireAsync(
        CancellationToken cancellationToken
    ) => _provider.Kind == NestedSetProviderKind.Sqlite
        ? AcquireCoreAsync(cancellationToken)
        : NestedSetTelemetry.MeasureLockAsync(AcquireCoreAsync, cancellationToken);

    /// <summary>Runs the provider-specific reservation and locking statements.</summary>
    private async Task AcquireCoreAsync(
        CancellationToken cancellationToken
    )
    {
        var created = false;

        if (_request.Mode == NestedSetTreeLockMode.New)
        {
            if (_provider.Kind == NestedSetProviderKind.SqlServer)
            {
                created = await ReserveSqlServerAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var affected = await ExecuteAsync(CreateSql(), cancellationToken).ConfigureAwait(false);

                if (_provider.Kind == NestedSetProviderKind.MySql)
                {
                    created = await QueryRevisionAsync(cancellationToken).ConfigureAwait(false) == 0;
                }
                else
                {
                    created = affected == 1;
                }
            }
        }
        else if (_provider.Kind == NestedSetProviderKind.Sqlite)
        {
            // WHY: A SQLite SELECT cannot acquire a row write lock. The no-op update enters the same writer
            // transaction used by every later structural statement without changing the revision.
            await ExecuteAsync(UpdateSql(), cancellationToken).ConfigureAwait(false);
        }

        var rows = await QueryLifecycleAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            var code = _request.Mode is NestedSetTreeLockMode.Existing or NestedSetTreeLockMode.Tombstoned
                ? NestedSetErrorCode.TreeNotFound
                : NestedSetErrorCode.LockAcquisitionFailed;

            // WHY: An existing-tree request can legitimately discover a missing registry row. A create request
            // already attempted to reserve that row, so absence here means the write lock was not acquired.
            throw new NestedSetException(
                code,
                code == NestedSetErrorCode.TreeNotFound
                    ? "The selected tree has no active registry row."
                    : "The tree registry row could not be reserved and locked.");
        }

        var lifecycle = rows[0];

        if (lifecycle == NestedSetTreeRegistryMetadata.Tombstoned)
        {
            if (_request.Mode == NestedSetTreeLockMode.Tombstoned)
            {
                return;
            }

            throw new NestedSetException(
                NestedSetErrorCode.TreeIdUnavailable,
                "The selected TreeId is reserved by a deleted tree.");
        }

        if (lifecycle != NestedSetTreeRegistryMetadata.Active)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidStructure,
                $"The selected tree has unsupported lifecycle value '{lifecycle}'.");
        }

        if (_request.Mode == NestedSetTreeLockMode.Tombstoned)
        {
            throw new NestedSetException(
                NestedSetErrorCode.TreeIdNotTombstoned,
                "The selected TreeId is active and cannot be purged.");
        }

        if (_request.Mode == NestedSetTreeLockMode.New
            && !created)
        {
            throw new NestedSetException(
                NestedSetErrorCode.TreeIdUnavailable,
                "The selected TreeId is already active.");
        }
    }

    /// <summary>Executes one parameterized registry write with the identity's exact relational mappings.</summary>
    private async Task<int> ExecuteAsync(
        string statement,
        CancellationToken cancellationToken
    )
    {
        var parameters = CreateParameters();

        return await _context
            .Database
            .ExecuteSqlRawAsync(statement, parameters, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads lifecycle through a provider locking query without materializing an infrastructure entity.
    /// </summary>
    private Task<List<byte>> QueryLifecycleAsync(
        CancellationToken cancellationToken
    )
    {
        var hint = _provider.Kind == NestedSetProviderKind.SqlServer
            ? " WITH (UPDLOCK, HOLDLOCK, ROWLOCK)"
            : string.Empty;

        var suffix = _provider.Kind switch
        {
            NestedSetProviderKind.PostgreSql => " FOR NO KEY UPDATE",
            NestedSetProviderKind.MySql => " FOR UPDATE",
            _ => string.Empty,
        };

        var value = _context
            .GetService<ISqlGenerationHelper>()
            .DelimitIdentifier("Value");

        var statement = $"SELECT {_lifecycle} AS {value} FROM {_table}{hint} WHERE {Predicate()}{suffix}";

        return _context
            .Database
            .SqlQueryRaw<byte>(statement, CreateParameters())
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads the revision needed to distinguish MySQL insertion from matched-row duplicate updates.</summary>
    private async Task<long> QueryRevisionAsync(
        CancellationToken cancellationToken
    )
    {
        var value = _context
            .GetService<ISqlGenerationHelper>()
            .DelimitIdentifier("Value");

        var statement = $"SELECT {_revision} AS {value} FROM {_table} WHERE {Predicate()} FOR UPDATE";
        var revisions = await _context
            .Database
            .SqlQueryRaw<long>(statement, CreateParameters())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return revisions.Count == 1 ? revisions[0] : -1;
    }

    /// <summary>Reserves one SQL Server key without locking unrelated gaps in an empty registry.</summary>
    private async Task<bool> ReserveSqlServerAsync(
        CancellationToken cancellationToken
    )
    {
        await using var command = _context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var created = command.CreateParameter();
        created.ParameterName = "created";
        created.DbType = DbType.Int32;
        created.Direction = ParameterDirection.Output;
        var parameters = CreateParameters()
            .Append(created)
            .ToArray();

        var output = $"{{{parameters.Length - 1}}}";
        var statement = $"SET NOCOUNT ON; BEGIN TRY {DirectInsertSql()}; SET {output} = 1; END TRY "
            + "BEGIN CATCH IF ERROR_NUMBER() NOT IN (2601, 2627) THROW; "
            + $"SET {output} = 0; END CATCH";

        // WHY: A serializable absence probe locks the sole gap of an empty registry and blocks unrelated trees.
        // The unique key remains the race arbiter: equal identities wait and report a duplicate, while distinct
        // identities insert independently. An output parameter avoids leaving a result reader open during rollback.
        // Catching only SQL Server's two duplicate-key numbers preserves every unrelated database error.
        await _context
            .Database
            .ExecuteSqlRawAsync(statement, parameters, cancellationToken)
            .ConfigureAwait(false);

        return created.Value is 1;
    }

    /// <summary>Builds a first-use-safe insert whose affected count distinguishes creation from reuse.</summary>
    private string CreateSql()
    {
        var columns = _scope is null
            ? $"{_treeId}, {_revision}, {_lifecycle}"
            : $"{_scope}, {_treeId}, {_revision}, {_lifecycle}";

        var values = _scope is null
            ? $"{{0}}, 0, {NestedSetTreeRegistryMetadata.Active}"
            : $"{{0}}, {{1}}, 0, {NestedSetTreeRegistryMetadata.Active}";

        if (_provider.Kind == NestedSetProviderKind.MySql)
        {
            // WHY: Doka requires MySQL matched-row reporting, so affected rows cannot distinguish insert from
            // duplicate. A revision marker distinguishes a conflicting reservation and is rolled back with
            // the rejected operation, preserving the active tree's committed revision.
            return $"INSERT INTO {_table} ({columns}) VALUES ({values}) "
                + $"ON DUPLICATE KEY UPDATE {_revision} = {_revision} + 1";
        }

        var conflict = _scope is null ? _treeId : $"{_scope}, {_treeId}";

        return $"INSERT INTO {_table} ({columns}) VALUES ({values}) ON CONFLICT ({conflict}) DO NOTHING";
    }

    /// <summary>Builds the provider-neutral direct insert used by SQL Server's duplicate-key handler.</summary>
    private string DirectInsertSql()
    {
        var columns = _scope is null
            ? $"{_treeId}, {_revision}, {_lifecycle}"
            : $"{_scope}, {_treeId}, {_revision}, {_lifecycle}";

        var values = _scope is null
            ? $"{{0}}, 0, {NestedSetTreeRegistryMetadata.Active}"
            : $"{{0}}, {{1}}, 0, {NestedSetTreeRegistryMetadata.Active}";

        return $"INSERT INTO {_table} ({columns}) VALUES ({values})";
    }

    /// <summary>Builds SQLite's no-op writer statement for an already active registry row.</summary>
    private string UpdateSql() => $"UPDATE {_table} SET {_revision} = {_revision} WHERE {Predicate()}";

    /// <summary>Builds the complete Scope/TreeId equality predicate using positional parameters.</summary>
    private string Predicate() => _scope is null
        ? $"{_treeId} = {{0}}"
        : $"{_scope} = {{0}} AND {_treeId} = {{1}}";

    /// <summary>Creates provider parameters through the registry's copied relational type mappings.</summary>
    private object[] CreateParameters()
    {
        using var command = _context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var mapping = _request.Mapping;

        if (mapping.Scope is null)
        {
            return
            [
                mapping
                    .TreeId
                    .GetRelationalTypeMapping()
                    .CreateParameter(command, "treeId", _request.TreeIdValue, nullable: false),
            ];
        }

        return
        [
            mapping
                .Scope
                .GetRelationalTypeMapping()
                .CreateParameter(command, "scope", _request.ScopeValue, nullable: false),
            mapping
                .TreeId
                .GetRelationalTypeMapping()
                .CreateParameter(command, "treeId", _request.TreeIdValue, nullable: false),
        ];
    }
}
