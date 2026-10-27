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

    /// <summary>Gets the caller context used by the selected native registry implementation.</summary>
    internal DbContext Context => _context;

    /// <summary>Gets the complete typed identity and lifecycle requirement.</summary>
    internal INestedSetTreeLockRequest Request => _request;

    /// <summary>Gets the delimited registry table.</summary>
    internal string Table => _table;

    /// <summary>Gets the optional delimited scope column.</summary>
    internal string? Scope => _scope;

    /// <summary>Gets the delimited tree identity column.</summary>
    internal string TreeId => _treeId;

    /// <summary>Gets the delimited revision column.</summary>
    internal string Revision => _revision;

    /// <summary>Gets the delimited lifecycle column.</summary>
    internal string Lifecycle => _lifecycle;

    /// <summary>Acquires one row lock and enforces active, unused, and tombstoned lifecycle rules.</summary>
    /// <param name="cancellationToken">The token used while waiting for and acquiring the database lock.</param>
    /// <returns>A task that completes after the transaction owns the registry row lock.</returns>
    internal Task AcquireAsync(
        CancellationToken cancellationToken
    ) => _provider.MeasureTreeRowLockWait
        ? NestedSetTelemetry.MeasureLockAsync(AcquireCoreAsync, cancellationToken)
        : AcquireCoreAsync(cancellationToken);

    /// <summary>Runs the provider-specific reservation and locking statements.</summary>
    private async Task AcquireCoreAsync(
        CancellationToken cancellationToken
    )
    {
        var created = false;

        if (_request.Mode == NestedSetTreeLockMode.New)
        {
            created = await _provider.ReserveTreeAsync(this, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _provider.LockExistingTreeAsync(this, cancellationToken).ConfigureAwait(false);
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
    internal async Task<int> ExecuteAsync(
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
        var value = _context
            .GetService<ISqlGenerationHelper>()
            .DelimitIdentifier("Value");

        var statement = _provider.TreeLifecycleReadSql(this, value);

        return _context
            .Database
            .SqlQueryRaw<byte>(statement, CreateParameters())
            .ToListAsync(cancellationToken);
    }

    /// <summary>Builds the mapped direct insert used by native reservation handlers.</summary>
    internal string DirectInsertSql()
    {
        var columns = _scope is null
            ? $"{_treeId}, {_revision}, {_lifecycle}"
            : $"{_scope}, {_treeId}, {_revision}, {_lifecycle}";

        var values = _scope is null
            ? $"{{0}}, 0, {NestedSetTreeRegistryMetadata.Active}"
            : $"{{0}}, {{1}}, 0, {NestedSetTreeRegistryMetadata.Active}";

        return $"INSERT INTO {_table} ({columns}) VALUES ({values})";
    }

    /// <summary>Builds the complete Scope/TreeId equality predicate using positional parameters.</summary>
    internal string Predicate() => _scope is null
        ? $"{_treeId} = {{0}}"
        : $"{_scope} = {{0}} AND {_treeId} = {{1}}";

    /// <summary>Creates provider parameters through the registry's copied relational type mappings.</summary>
    internal object[] CreateParameters()
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
