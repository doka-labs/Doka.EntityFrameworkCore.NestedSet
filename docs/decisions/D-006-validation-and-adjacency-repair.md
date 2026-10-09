---
id: D-006
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Structural inspection, repair authority, memory cost, and atomic repair batches"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-006 -- Validate iteratively and repair derived structure from adjacency

## Context and Problem Statement

Stored bounds can become inconsistent after external writes or failed application procedures. Rebuild needs an
authoritative input that does not depend on already damaged intervals. The implementation inspects compact
structural rows and derives bounds, depth, and dense positions from valid parent links and the applicable sibling
order.

## Decision Drivers

- Detect cycles, unknown parents, invalid bounds, and ambiguous adjacency before repair writes.
- Avoid recursion depth failures and materializing domain payload for structural inspection.
- Write only changed repairs in bounded batches under one operation boundary.
- State the retained O(n) memory and inspection cost explicitly.

## Considered Options

- Iterative adjacency inspection with changed-row repair batches
- Rebuild directly from existing nested-set bounds
- Recursive domain-entity traversal with per-row EF updates

## Decision Outcome

Chosen option: "Iterative adjacency inspection with changed-row repair batches", because persisted adjacency is a
separate source for derived coordinates and an iterative inspector handles deep hierarchies without recursive stack
growth.

### Consequences

- Good, because repair can restore bounds and depth without changing node identities or inventing missing parent relationships.
- Bad, because large scopes require full structural inspection, and invalid adjacency cannot be safely repaired automatically.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx -c Release --filter "FullyQualifiedName~InspectionTests|FullyQualifiedName~RelationalTests|FullyQualifiedName~EnterpriseTests"` and expect inspection and rebuild tests to pass.
- Expect cycles, missing parents, and ambiguous manual positions to reject before updates. Test cancellation during inspection and repair batches; expect rollback to preserve the prior database state. A no-change rebuild must not rewrite every row.
- Require the SQLite repair UPDATE to seek bounded unique keys without manually
  running ANALYZE. Include scoped composite and converted unscoped keys, excluded
  trees/scopes, and a physical table name matching the internal membership alias.

## Pros and Cons of the Options

### Iterative adjacency inspection with changed-row repair batches

- Good, because valid parent links provide a recovery source even when bounds and depth are damaged.
- Bad, because the complete scoped structure still requires O(n) memory and ambiguous parents or manual positions need an application decision.

### Rebuild directly from existing nested-set bounds

- Good, because a valid interval ordering could reduce dependence on separately persisted parent roles.
- Bad, because damaged intervals would become both the detected problem and the reconstruction authority.

### Recursive domain-entity traversal with per-row EF updates

- Good, because ordinary entities and recursive code can be straightforward for small, trusted trees.
- Bad, because deep trees risk stack exhaustion, payload increases memory, and per-node writes increase database round trips.

## More Information

Strict ordering can reconstruct sibling positions from its database rule. Manual and flexible modes preserve stored
order and reject negative or duplicate positions when that order is ambiguous. Validation selects a consistent read
boundary; rebuild shares the mutation lock. Bounded write batches do not imply bounded total inspection memory.

For SQLite keyed writes, an outer Scope/NodeKey predicate finds the batch and a
correlated unique-key probe validates TreeId in the same statement. This avoids
a repeated complete-tree scan selected for fresh, unanalyzed databases while
preserving native column equality. Existing required unique keys back both
lookups; no new index or migration is introduced. The internal membership alias
must differ from the physical outer table name under SQLite identifier equality.

### Re-evaluation Triggers

- An application requires inspection of scopes larger than the agreed memory budget.
- A new repair proposal changes parent links or resolves ambiguous adjacency automatically.
- Provider comparison rules require a new parent-key canonicalization strategy.
- An actual repair plan scans a tree for each bounded key batch or loses exact
  Scope/TreeId membership on an accepted physical mapping.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed iterative adjacency inspection, changed-row repair batches, and validation and rebuild regression specifications against the linked repository evidence.
- 2026-10-03: Real million-row qualification exposed a SQLite complete-tree scan per repair batch. Separated unique-key access from statement-local membership checks and added plan/isolation regressions without manual statistics.

### Implementation References

- [Iterative inspector](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Maintenance/NestedSetInspector.cs)
- [Maintenance coordinator](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Maintenance/NestedSetMaintenance.cs)
- [Repair writer](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Maintenance/NestedSetRepairWriter.cs)
- [Shared batch limit](../../src/Doka.EntityFrameworkCore.NestedSet/Storage/NestedSetBatch.cs)
- [Inspector tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Validation)
- [Rebuild tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Hierarchy/Maintenance/RelationalTests.Rebuild.cs)
- [SQLite keyed write plans and isolation](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Indexes/SqliteQueryPlanTests.cs)
- [Implementation design](../../docs/implementation-design.md)

### Sources

- [SQLite optimizer overview](https://sqlite.org/optoverview.html) (primary source; retrieved 2026-10-03)
- [SQLite EXPLAIN QUERY PLAN](https://sqlite.org/eqp.html) (primary source; retrieved 2026-10-03)
