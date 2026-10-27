namespace Doka.EntityFrameworkCore.NestedSet.SqlServer;

internal sealed partial class SqlServerNestedSetProvider
{
    /// <inheritdoc />
    internal override IsolationLevel RequiredIsolation => IsolationLevel.ReadCommitted;

    /// <inheritdoc />
    internal override bool SupportsStatementAtomicSelfReferentialDelete => true;

    /// <inheritdoc />
    internal override bool IsSafeReadSnapshot(
        IsolationLevel? isolation
    ) => isolation is IsolationLevel.Serializable or IsolationLevel.Snapshot;

    /// <inheritdoc />
    internal override bool HasStatementReadSnapshot(
        IsolationLevel? isolation
    ) => false;

    /// <inheritdoc />
    internal override IsolationLevel ReadSnapshotIsolation => IsolationLevel.Serializable;

    /// <inheritdoc />
    internal override async Task<bool> ReserveTreeAsync(
        NestedSetTreeLock treeLock,
        CancellationToken cancellationToken
    )
    {
        await using var command = treeLock
            .Context
            .Database
            .GetDbConnection()
            .CreateCommand();

        var created = command.CreateParameter();
        created.ParameterName = "created";
        created.DbType = DbType.Int32;
        created.Direction = ParameterDirection.Output;
        var parameters = treeLock
            .CreateParameters()
            .Append(created)
            .ToArray();

        var output = $"{{{parameters.Length - 1}}}";
        var statement = $"SET NOCOUNT ON; BEGIN TRY {treeLock.DirectInsertSql()}; SET {output} = 1; END TRY "
            + "BEGIN CATCH IF ERROR_NUMBER() NOT IN (2601, 2627) THROW; "
            + $"SET {output} = 0; END CATCH";

        // WHY: A serializable absence probe locks the sole gap of an empty registry and blocks unrelated trees.
        // Equal identities race on the unique key while distinct identities insert independently. The output
        // parameter avoids leaving a result reader open during rollback; only duplicate-key errors are caught.
        await treeLock
            .Context
            .Database
            .ExecuteSqlRawAsync(statement, parameters, cancellationToken)
            .ConfigureAwait(false);

        return created.Value is 1;
    }

    /// <inheritdoc />
    internal override string TreeLifecycleReadSql(
        NestedSetTreeLock treeLock,
        string valueAlias
    ) => $"SELECT {treeLock.Lifecycle} AS {valueAlias} FROM {treeLock.Table} WITH (UPDLOCK, HOLDLOCK, ROWLOCK) "
        + $"WHERE {treeLock.Predicate()}";

    /// <inheritdoc />
    internal override async Task ValidateCallerSessionAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        // WHY: A duplicate-key failure with XACT_ABORT ON dooms the caller transaction beyond savepoint
        // recovery. Reject that session before any registry lock; 16384 is its documented @@OPTIONS bit.
        var options = await context
            .Database
            .SqlQueryRaw<int>("SELECT @@OPTIONS & 16384 AS [Value]")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        if (options != 0)
        {
            throw new NestedSetException(
                NestedSetErrorCode.InvalidTransaction,
                "SQL Server caller transactions must use SET XACT_ABORT OFF for nested-set savepoints.");
        }
    }

    /// <inheritdoc />
    internal override string CollationSql(
        ISqlGenerationHelper sql,
        string collation
    )
    {
        // WHY: COLLATE takes a bare collation token rather than a bracket-delimited identifier.
        if (collation.Length == 0
            || collation.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException(
                "SQL Server collation names must contain only ASCII letters, digits, and underscores.");
        }

        return collation;
    }
}
