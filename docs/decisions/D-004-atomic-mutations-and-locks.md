---
id: D-004
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Mutation atomicity, caller transactions, typed tree locks, retries, and failure ownership"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-004 -- Serialize writers through typed per-tree registry locks

## Context and Problem Statement

A nested-set mutation changes intervals, adjacency, positions, registry state,
and sometimes two trees. Concurrent writers that calculate gaps from different
snapshots can corrupt both trees. Applications also need hierarchy work to
commit atomically with domain writes and may configure retrying EF execution
strategies.

## Decision Drivers

- Serialize writers for the same complete tree identity.
- Permit independent server writers for unrelated trees.
- Preserve application ownership of an existing transaction and final commit.
- Lock both sides of cross-tree operations in one stable database order.
- Recheck changed nodes and Parent endpoints after locking before saving payload.
- Support retries only around the complete repeatable application transaction.
- Preserve definite rollback and expose unknown commit outcomes.

## Considered Options

- Typed registry row per Scope and TreeId with one transaction protocol
- Table-wide or Scope-wide writer lock
- Optimistic revision checks with isolated automatic retries
- Application-owned raw SQL lock coordination

## Decision Outcome

Chosen option: "Typed registry row per Scope and TreeId with one transaction
protocol", because database equality and ordering must follow the configured
store types, converters, and collations. A caller transaction is protected by a
savepoint and remains caller-owned. Without one, the library owns the complete
transaction. A coordinated save checks every changed node's current tree
identity after acquiring its planned locks, before persisting payload. On SQL
Server, a caller transaction with `XACT_ABORT ON` is rejected before the
savepoint: a caught duplicate-key error can otherwise make the whole
transaction uncommittable, so a savepoint could not preserve earlier work.

### Consequences

- Good, because same-tree writers serialize while independent server trees do not share a library-level global lock.
- Good, because hierarchy and domain writes can commit atomically in one application transaction.
- Bad, because a hot tree remains a serialized write partition and nested-set interval shifts are still O(N) in the database.
- Bad, because SQLite retains its database-level writer limit despite the tree-local public contract.
- Bad, because SQL Server caller mutations require one session-option read and reject `XACT_ABORT ON`; applications using that mode must turn it off before the hierarchy call.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx --filter "FullyQualifiedName~RetryBoundaryTests|FullyQualifiedName~TreeRegistry|FullyQualifiedName~Concurrent|FullyQualifiedName~Transaction"` and expect first-writer races, same-tree serialization, distinct-tree overlap, counter-direction cross-tree moves, caller rollback, cancellation, and unknown commit cases to pass on every qualified provider.
- The retry-boundary cases must reject a transaction created outside the configured execution-strategy delegate and pass when the caller creates its transaction inside the active complete-unit delegate.
- The SQL Server caller-transaction cases must reject `XACT_ABORT ON` before a hierarchy write and show that earlier caller work can still commit. A coordinated save must reject a node that moved to an unlocked tree before payload persistence.

## Pros and Cons of the Options

### Typed registry row per Scope and TreeId with one transaction protocol

- Good, because typed database identity avoids CLR hash, formatting, and collation disagreement.
- Bad, because each hierarchy adds an internal migration table and tombstone lifecycle that operators must monitor.

### Table-wide or Scope-wide writer lock

- Good, because the identity and acquisition protocol are simple.
- Bad, because unrelated trees block one another and cannot meet the independent-writer target.

### Optimistic revision checks with isolated automatic retries

- Good, because low-contention writers might avoid holding a lock during planning.
- Bad, because repeating only structural SQL can duplicate or detach it from domain side effects, and an unknown commit cannot be made safe without application idempotency.

### Application-owned raw SQL lock coordination

- Good, because an expert application could optimize for a known deployment.
- Bad, because every consumer would have to reproduce provider locking, first-tree creation, savepoint, rollback, and tombstone semantics.

## More Information

Registry keys are `TreeId` or `(Scope, TreeId)`. Revision changes with
structure; Lifecycle preserves active and tombstoned identities. Cross-tree
operations ask the database to order typed identities before acquisition.
The registry's physical table name derives from the hierarchy schema, table,
and identity column names. CLR entity renames therefore leave the registry
table intact. Databases created with the earlier CLR-name-based hash require
the data-preserving table rename described in the migration guide.
Rollback cleanup uses `CancellationToken.None` after forward progress fails.
This prevents request cancellation from suppressing recovery, not provider
rollback timeouts. SqlClient 6.1.6's native transaction rollback uses the
connection timeout rather than EF's ordinary command timeout. Applications
must budget recovery for their largest transaction. If rollback or disposal
fails, preserve both errors, discard the context and caller transaction, and
reconcile through a fresh connection; client timeout does not prove server undo
has finished. Capacity fixtures remove this implicit undo-speed gate only for
their functional tests, retaining ordinary command deadlines and every exact
rollback assertion.
A connection loss during commit requires a fresh-context reconciliation. The
only supported registry-row deletion is `PurgeTreeIdAsync`: it follows the same
transaction and exact-identity lock protocol, rejects active identities, and
deletes a tombstone only after verifying that no hierarchy rows remain.

Insertion recovery owns the exact introduced root and owned entries, including
an initial transition rejected by an application tracking callback. An entry
can remain Detached while EF already holds a reference or only some of its key
registrations. Reassigning Detached cannot release those registrations.
Callbacks can also replace or mutate a key after its native map installation.

Single insertion completes public detachment before the owned commit or caller
savepoint release. A rejecting `StateChanging` or `StateChanged` callback can
therefore roll back the INSERT. Recovery restores captured generated CLR
leaves on the root, complex values, and owned payload. It also restores owned
keys and ownership foreign keys captured before EF's relationship fixup,
including assigned keys whose `ValueGenerated` is `Never`. Ordinary payload
and non-ownership business foreign keys remain caller-owned. Initialization
first retains the bounded owned graph, so a throwing application getter or tracking callback cannot leave a
previously introduced owned reference behind. Caller-tracked foreign entries
are never treated as entries introduced by the insertion.

The shared insertion lifecycle uses comparer snapshots and exact-entry native
removal for those cases. It captures generated identities at the existing
relational persistence boundary, before acceptance or `SavedChanges`; the
managed path verifies CLR and sidecar representations before acceptance can
hide an edit. Ordinary detachment remains the first lifecycle step. Exceptional
cleanup removes only the introduced entry's surviving reference, key, and
dependent memberships, preserving unrelated entries and legitimate collisions.

This is a deliberate EF infrastructure seam: public state assignment cannot
remove a never-tracked reference or an installed key whose representation was
changed by a callback. The required contracts are qualified against EF
10.0.12, resolved once, and used through cached delegates in the normal path.
One model-free binding owner separates these contracts from entry lifecycle
state. `UseNestedSets` validates every required signature, including closed
scalar, nullable-key, and actual `IReadOnlyList<object?>` composite map shapes,
before registering the extension. Metadata validation does not compile unused
probe operations; native closed map operations compile on first model use and
remain weakly cached. Incompatibility names the loaded EF version and exact member before
any context writes; it does not require discarding a nonexistent context.
An explicit contract test localizes framework changes instead of relying on
many insertion failures to reveal them.
They are not covered by EF's public compatibility promise. When database
rollback succeeds, failed identity restoration still produces the existing
aggregate cleanup error and discard-context instruction. Clearing the caller's
tracker, synthetic lifecycle transitions, and global map scans are rejected
alternatives because they alter unrelated application state or callback
behavior.

Unchanged identity matching and detach preparation reuse the same typed readers
as refresh. Current values alone are insufficient: change detection can install
an intermediate relationship key even when a callback subsequently restores
the original CLR value. The native generic relationship-snapshot reader is
therefore validated once and compiled once per property reader. EF's public
`IUpdateEntry` route exposes only an object snapshot getter; that public route
remains appropriate when capturing actual changed vectors. Differing CLR and
model member types keep EF's object conversion semantics, including sentinels.
Typed comparer expressions preserve null protection for both operands.
Dependent-bucket storage is allocated only for a real native membership.
The initial-root and warmed comparison budgets qualify these isolated paths;
they do not replace complete-import heap or hosted capacity evidence.

Recovery audits only the distinct touched maps once for exact introduced
entries that survived cleanup. This failure-only read uses EF's native map
enumeration, with bounded active-batch memory and linear time in the entries
of those maps. It detects stale hash slots after a callback explicitly rekeys
a mutable identity through change detection and mutates it again. Such a slot
cannot be repaired through exact key operations; it produces the aggregate
recovery failure and discard-context requirement, preserving the original
guard error and definite database rollback. The successful insertion path does
not enumerate these maps.

### Re-evaluation Triggers

- A supported provider changes row-lock, transaction, savepoint, or execution-strategy behavior.
- A provider changes native rollback timeout or cancellation behavior. Requalify full-size late failure and cancellation with actual database, registry, input and tracker restoration, retaining recovery failure classification.
- Query-plan evidence shows a registry statement takes locks outside its exact identity.
- A tested idempotency and commit-verification contract makes optimistic replay safe for a complete application unit.
- EF changes reference registration, identity-map or dependent-map cleanup, generated-value acceptance, or the insertion lifecycle contracts. Requalify initial tracking rejection, mutable and generated keys, owned payload, unrelated identities, retry, and definite database rollback before upgrading.
- EF introduces a public typed relationship-snapshot getter or changes its native generic signature, composite key shape, sentinel conversion, or comparer null handling. Revisit the reader seam and rerun signature, allocation, intermediate-key, sidecar, property-bag, and exact cleanup controls.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: The proposal documented the earlier shared writer boundary for retrospective comparison.
- 2026-09-23: Typed per-tree registry locks, database-ordered cross-tree acquisition, caller savepoints, and complete-unit retry ownership were confirmed.
- 2026-09-23: Status changed from proposed to accepted.
- 2026-09-23: Documented physical registry naming and its one-time legacy table-rename path after the CLR-rename regression review.
- 2026-09-24: Saves recheck changed nodes after locking and reuse the outer boundary for Parent moves.
- 2026-09-24: Required `XACT_ABORT OFF` in SQL Server caller transactions after verifying that `ON` can invalidate savepoint rollback on a caught duplicate-key error.

- 2026-09-28: The maintainer confirmed acceptance and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed typed registry locks, caller savepoints, complete-unit retries, tracker recovery, and provider regression specifications against the linked repository evidence.
- 2026-10-04: Qualified exact insertion identity recovery after initial tracking rejection and callback key mutation. Preserved the existing persistence boundary, caller tracker ownership, and explicit framework-seam failure policy.
- 2026-10-04: Separated insertion framework bindings from lifecycle state and moved their cached compatibility check to options registration, with member-specific startup diagnostics and an explicit framework-contract regression.
- 2026-10-04: Diagnosed SqlClient's native connection-timeout boundary during million-row undo. Kept runtime recovery and aggregate error preservation unchanged; separated the functional capacity fixture from implicit rollback timing gates and documented application-owned recovery budgets.
- 2026-10-08: Removed unchanged-path boxing and eager bucket storage, preserved installed relationship snapshots and native property-bag diagnostics, and separated metadata-only startup validation from model-consumed delegate compilation. Added allocation, semantic, lazy-cache, and actual registration controls.

### Implementation References

- [Mutation executor](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetMutationExecutor.cs)
- [Transaction boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTransaction.cs)
- [Tree locks](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTreeLocks.cs)
- [Bounded insertion identity lifecycle](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionTracking.cs)
- [Insertion framework binding contract](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionContract.cs)
- [Insertion-owned CLR rollback values](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionValues.cs)
- [Insertion comparison and capture budgets](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Comparisons.cs)
- [Insertion recovery semantics](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Recovery.cs)
- [Insertion startup and native map contracts](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/InsertionReflectionContractTests.cs)
- [Provider-result boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetRelationalDatabase.cs)
- [Single insertion callback regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert)
- [Single detachment rollback and retry](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.Detachment.cs)
- [Single initialization cleanup](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.Initialization.cs)
- [Legitimate identity collision recovery](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.Collision.cs)
- [Bulk native identity regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkIdentityRetentionTests.cs)
- [Failed tracking recovery regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkStagingRetentionTests.cs)
- [Typed registry convention](../../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetInfrastructureConvention.cs)
- [Tree identity purge](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Maintenance/NestedSetTreeRegistryPurger.cs)
- [Retry tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Transactions/RetryBoundaryTests.cs)
- [Tree-registry concurrency tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/TreeRegistry)
- [Tree identity purge tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Purge.cs)
- [Registry rename tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/TableMappings/RegistryMigrationProviderTests.cs)
- [Transaction guide](../../docs/transactions-and-locking.md)
- [Full-size failure and cancellation controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Capacity/CapacityTests.cs)
- [Registry migration guide](../../docs/migrations.md)

### Sources

- [EF Core 10.0.12 public object relationship-snapshot getter](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/IUpdateEntry.cs#L156) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 native typed relationship-snapshot getter](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L514) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 composite principal-key factory](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/CompositePrincipalKeyValueFactory.cs#L12) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 key type selection](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/IKey.cs#L30) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 sentinel-aware property access](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/PropertyAccessorsFactory.cs#L236) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 CLR getter conversion](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/ClrPropertyGetterFactory.cs#L164) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 sentinel null guards](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Extensions/Internal/ExpressionExtensions.cs#L56) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 comparer null guards](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/ValueComparer%60.cs#L239) (primary source; retrieved 2026-10-08)
- [SqlClient 6.1.6 native rollback timeout selection](https://github.com/dotnet/SqlClient/blob/b862260e80dd3d159b812cbe5c51b933fe348afe/src/Microsoft.Data.SqlClient/netcore/src/Microsoft/Data/SqlClient/SqlInternalConnectionTds.cs#L1190-L1193) (primary source; retrieved 2026-10-04)
- [EF Core 10.0.12 native rollback forwarding](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalTransaction.cs#L209-L254) (primary source; retrieved 2026-10-04)
- [.NET default async rollback cancellation behavior](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbtransaction.rollbackasync?view=net-10.0) (primary source; retrieved 2026-10-04)
- [EF Core 10.0.12 state manager and reference lifecycle](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/StateManager.cs) (primary source; retrieved 2026-10-03)
- [EF Core 10.0.12 exact identity-map removal](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/IdentityMap.cs) (primary source; retrieved 2026-10-03)
- [EF Core value comparers and key snapshots](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers) (primary source; retrieved 2026-10-03)
- [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) (primary source; retrieved 2026-09-23)
- [EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency) (primary source; retrieved 2026-09-23)
- [MySQL locking reads](https://dev.mysql.com/doc/refman/8.4/en/innodb-locking-reads.html) (primary source; retrieved 2026-09-23)
- [PostgreSQL explicit locking](https://www.postgresql.org/docs/current/explicit-locking.html) (primary source; retrieved 2026-09-23)
- [PostgreSQL Read Committed](https://www.postgresql.org/docs/current/transaction-iso.html) (primary source; retrieved 2026-09-24)
- [SQLite isolation](https://www.sqlite.org/isolation.html) (primary source; retrieved 2026-09-23)
- [SQL Server table hints](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table) (primary source; retrieved 2026-09-23)
- [SQL Server TRY...CATCH and uncommittable transactions](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/try-catch-transact-sql) (primary source; retrieved 2026-09-24)
- [SQL Server SET XACT_ABORT](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-xact-abort-transact-sql) (primary source; retrieved 2026-09-24)
- [EF Core migration customization](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing) (primary source; retrieved 2026-09-23)
