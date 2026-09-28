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

Live-provider cases become executable with each owning provider project. A
source/unit-only run does not qualify database behavior.

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
A connection loss during commit requires a fresh-context reconciliation. The
only supported registry-row deletion is `PurgeTreeIdAsync`: it follows the same
transaction and exact-identity lock protocol, rejects active identities, and
deletes a tombstone only after verifying that no hierarchy rows remain.

### Re-evaluation Triggers

- A supported provider changes row-lock, transaction, savepoint, or execution-strategy behavior.
- Query-plan evidence shows a registry statement takes locks outside its exact identity.
- A tested idempotency and commit-verification contract makes optimistic replay safe for a complete application unit.

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

### Implementation References

- [Mutation executor](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetMutationExecutor.cs)
- [Transaction boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTransaction.cs)
- [Tree locks](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTreeLocks.cs)
- [Typed registry convention](../../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetInfrastructureConvention.cs)
- [Tree identity purge](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Maintenance/NestedSetTreeRegistryPurger.cs)
- [Retry tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Transactions/RetryBoundaryTests.cs)
- [Tree-registry concurrency tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/TreeRegistry)
- [Tree identity purge tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Purge.cs)
- [Registry rename tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/TableMappings/RegistryMigrationProviderTests.cs)
- [Transaction guide](../../docs/transactions-and-locking.md)
- [Registry migration guide](../../docs/migrations.md)

### Sources

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
