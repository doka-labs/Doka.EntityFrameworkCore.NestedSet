---
id: D-010
status: implemented
date: 2026-09-24
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Deletion of qualified TPT, table-split, and entity-split hierarchies"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-010 -- Delete every mapped table fragment in bounded batches

## Context and Problem Statement

The configuration contract includes qualified TPT, table-split, and entity-split
hierarchies. Inserts and moves work, but all three deletion operations used EF
Core `ExecuteDeleteAsync`. EF Core cannot translate those multi-table or shared-
table deletions. The failure occurs after some structural updates, which the
existing transaction rolls back. The documented model shapes therefore need a
different physical delete path without changing the public API or transaction
boundary.

## Decision Drivers

- Preserve the documented mapping compatibility for all three delete operations.
- Keep deletion atomic with caller-owned transactions and tree registry locks.
- Avoid loading and tracking an entire large subtree in application memory.
- Preserve the one-statement path for single-table hierarchies with inline owned values.
- Do not depend on database cascade settings for mapped fragment deletion.

## Considered Options

- Delete mapped physical fragments in bounded key batches
- Delete tracked entities through SaveChanges
- Reject deletion for qualified multi-table mappings

## Decision Outcome

Chosen option: "Delete mapped physical fragments in bounded key batches", because
it preserves the supported model shapes and transaction contract while bounding
client memory and leaving the single-table delete path, including inline owned
values, unchanged when the hierarchy is the shared-table principal. The
operation selects only primary keys through the scoped EF query, deletes every
mapped fragment in dependent-before-principal order, and checks the structural
table's affected row count. Subtree and tree deletion unlink internal parent
references before the first batch. EF model metadata supplies table and column
names and the provider-specific parameter mapping.

### Consequences

- Good, because mapped payload fragments are deleted with their hierarchy row
  on every supported provider, without relying on cascade rules.
- Bad, because multi-table deletion needs a key read and at least one DELETE per
  table per batch; large deletes take more database round trips than a
  single-table `ExecuteDelete`. Tracked split payload instances remain stale,
  as with other immediate database deletes.

### Confirmation

Live-provider cases become executable with each owning provider project. A
source/unit-only run does not qualify database behavior.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx --filter FullyQualifiedName~MappingDeletionTests` and expect all provider cases to pass for node, subtree, and tree deletion. The cases verify physical row counts, surviving trees, and full structure validation.
- The same test class must cover a tree larger than one key batch and a mixed
  derived TPT tree on MySQL, MariaDB, PostgreSQL, SQLite, and SQL Server.
- The SQLite fragment-failure case must fail the delete and then find every
  original row and a valid tree, proving that earlier fragment deletes and
  parent unlinking rolled back.
- `OwnedPayloadDeletionTests` must confirm on all five providers that inline
  `OwnsOne` values keep the set-based EF delete path and that surviving owned
  values remain readable.
- Run `dotnet build Doka.EntityFrameworkCore.NestedSet.slnx --no-restore` with warnings as errors and expect no analyzer or compiler diagnostics.

## Pros and Cons of the Options

### Delete mapped physical fragments in bounded key batches

- Good, because physical tables can be ordered from EF foreign-key metadata,
  and only bounded key batches are materialized.
- Bad, because the library must maintain a SQL writer and a mapping-level
  regression matrix for this path.

### Delete tracked entities through SaveChanges

- Good, because EF would generate entity-specific delete commands and manage
  the mapped fragments itself.
- Bad, because tracking a large subtree raises memory use, emits per-row
  commands, and could save unrelated pending application changes at the wrong
  point in the hierarchy mutation.

### Reject deletion for qualified multi-table mappings

- Good, because the implementation remains small and fails before any writes.
- Bad, because it withdraws the documented compatibility of the public delete
  operations for these otherwise supported mapping forms.

## More Information

The batch size is at most 128 keys and is reduced for composite primary keys to
stay at or below 900 key parameters per statement. The entire sequence remains
inside the existing mutation transaction or caller savepoint. Application
tables outside the mapped hierarchy keep their own foreign-key policy; this
decision does not introduce domain cascades.

### Re-evaluation Triggers

- EF Core can translate deletion of these exact mapping forms without losing
  mapped fragments; compare the generated SQL and provider tests before
  removing the physical path.
- Representative large multi-table deletes exceed the measured latency or
  allocation budget; evaluate a provider-specific staged-key strategy with
  equivalent transaction and mapping tests.
- A supported mapping exposes a physical fragment dependency that EF's
  relational model cannot order; add a reproducible case before changing the
  ordering rule.

### Decision History

- 2026-09-24: Decision recorded with status proposed.
- 2026-09-24: The mapping-aware path and provider regressions were implemented; this is not owner acceptance.
- 2026-09-24: Inline owned values were excluded from the batch trigger and covered by provider tests.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed mapping-aware physical deletion, typed captured keys, table-fragment ordering, and mapping and rollback regression specifications against the linked repository evidence.

### Implementation References

- [Physical fragment delete](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Delete/NestedSetPhysicalDelete.cs)
- [Node and subtree deletion](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Delete/NestedSetDelete.cs)
- [Whole-tree deletion](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Delete/NestedSetTreeDeleter.cs)
- [Provider and rollback tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/Deletion/MappingDeletionTests.cs)
- [Inline owned deletion tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/Payloads/OwnedPayloadDeletionTests.cs)
- [Operation contract](../operations.md)
- [Capacity trade-off](../performance.md)

### Sources

- [EF Core ExecuteUpdate and ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete) (primary source; retrieved 2026-09-24)
- [EF Core 7.0 inheritance and multiple tables](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-7.0/whatsnew) (primary source; retrieved 2026-09-24)
- [EF Core advanced table mapping](https://learn.microsoft.com/en-us/ef/core/modeling/table-splitting) (primary source; retrieved 2026-09-24)
- [EF Core 8.0 support for owned types in ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew) (primary source; retrieved 2026-09-24)
