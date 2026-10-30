using System.Data.Common;

namespace Doka.EntityFrameworkCore.NestedSet.Providers;

internal abstract partial class NestedSetProviderCapabilities
{
    /// <summary>Gets the isolation required before lock-protected structure reads.</summary>
    internal abstract IsolationLevel RequiredIsolation { get; }

    /// <summary>Gets whether deleting every referencing row together satisfies restrictive self foreign keys.</summary>
    internal abstract bool SupportsStatementAtomicSelfReferentialDelete { get; }

    /// <summary>Gets whether the registry statement, rather than transaction creation, contains the writer wait.</summary>
    internal virtual bool MeasureTreeRowLockWait => true;

    /// <summary>Gets whether transaction creation acquires the hierarchy's database writer lock.</summary>
    internal virtual bool MeasureTransactionBeginLockWait => false;

    /// <summary>Reserves one complete identity and reports whether this operation created it.</summary>
    internal abstract Task<bool> ReserveTreeAsync(
        NestedSetTreeLock treeLock,
        CancellationToken cancellationToken
    );

    /// <summary>Acquires any required writer intent before reading an existing registry row.</summary>
    internal virtual Task LockExistingTreeAsync(
        NestedSetTreeLock treeLock,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    /// <summary>Builds the locking lifecycle read using already delimited identifiers and mapped parameters.</summary>
    internal abstract string TreeLifecycleReadSql(
        NestedSetTreeLock treeLock,
        string valueAlias
    );

    /// <summary>Gets the exact scalar or rowset parameter capacity required by one tree-order query.</summary>
    /// <param name="requestCount">The number of identities in the completed lock plan.</param>
    /// <param name="scoped">Whether each identity also includes a scope value.</param>
    /// <returns>
    /// The parameter capacity for the shared scalar threshold unless the native transport overrides it.
    /// </returns>
    internal virtual int TreeRowsetParameterCount(
        int requestCount,
        bool scoped
    ) => NestedSetRegistryRowset.ParameterCount(requestCount, scoped);

    /// <summary>Builds the native source for database-equal identity grouping and canonical lock ordering.</summary>
    internal abstract string BuildTreeRowsetSource(
        DbCommand command,
        List<object> parameters,
        NestedSetTreeRowsetAliases aliases,
        NestedSetTreeRegistryMapping mapping,
        INestedSetTreeLockRequest[] requests
    );

    /// <summary>Checks session requirements before a caller-owned savepoint can promise failure recovery.</summary>
    internal virtual Task ValidateCallerSessionAsync(
        DbContext context,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    /// <summary>Checks whether the caller already owns a stable snapshot covering every inspection statement.</summary>
    internal abstract bool IsSafeReadSnapshot(
        IsolationLevel? isolation
    );

    /// <summary>Checks whether one statement has a stable snapshot under the current transaction state.</summary>
    internal abstract bool HasStatementReadSnapshot(
        IsolationLevel? isolation
    );

    /// <summary>Gets the read-only transaction isolation covering a multi-statement inspection.</summary>
    internal abstract IsolationLevel ReadSnapshotIsolation { get; }

    /// <summary>Renders a trusted model collation using the native identifier grammar.</summary>
    /// <param name="sql">The actual provider helper that owns identifier quoting and escaping.</param>
    /// <param name="collation">The collation name configured in the model.</param>
    /// <returns>The delimited identifier, or an adapter-specific validated native token.</returns>
    internal virtual string CollationSql(
            ISqlGenerationHelper sql,
            string collation
        )
        // WHY: The actual provider helper owns identifier escaping; adapters override only different native grammar.
        => sql.DelimitIdentifier(collation);

    /// <summary>Validates ordering support shared by translated queries and mapped command windows.</summary>
    internal virtual void ValidateOrdering(
        NestedSetOrdering? order
    ) { }

    /// <summary>Builds an exact membership predicate while preserving the native keyed update access path.</summary>
    internal virtual string KeyBatchIdentityPredicate<TEntity, TKey, TTreeId, TScope>(
        NestedSetMappedBatch<TEntity, TKey, TTreeId, TScope> batch
    )
        where TEntity : class
        where TKey : notnull
        where TTreeId : notnull
        where TScope : notnull => batch.IdentityPredicate;
}
