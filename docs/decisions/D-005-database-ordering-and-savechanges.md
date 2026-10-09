---
id: D-005
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Sibling order modes, database comparisons, and SaveChanges coordination"
supersedes: []
superseded-by: []
amends: []
amended-by: [D-012]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-005 -- Integrate configured ordering with asynchronous saves

## Context and Problem Statement

Alphabetically ordered folders need sibling sorting without flattening the complete tree. Renaming a tracked folder
can change its position and subtree coordinates. The implementation offers manual, flexible, and strict order modes
and coordinates configured ordering with SaveChangesAsync.

## Decision Drivers

- Preserve preorder for full-tree reads and database comparison semantics for sibling keys.
- Allow applications to choose manual placement, flexible overrides, or a strict rule.
- Commit payload edits and structural reorder together.
- Preserve relevant EF tracker, callback, and acceptance semantics after success or failure.

## Considered Options

- Database ordering with explicit asynchronous save integration
- Read-time sorting in application memory
- Explicit reorder calls after every payload change

## Decision Outcome

Chosen option: "Database ordering with explicit asynchronous save integration", because the configured rule is a
persistence contract, and payload changes must not leave Position and intervals inconsistent. Mode selection keeps
manual placement available when requested.

### Consequences

- Good, because renaming a node can update its sibling position and complete subtree order in the same transaction as the payload.
- Bad, because configured saves cost more than a native payload-only EF save and require the documented asynchronous context integration.

An inherited hierarchy is coordinated through its exact configured owner,
including changed and tracked sibling derived types. Grouping by concrete
entry metadata would restrict Parent resolution and structural updates to one
subtype and cannot apply an ordering selector declared on the common owner.
The owner is cached with the EF model; named shared mappings and separately
configured concrete stores remain distinct.

Refresh metadata includes generated concurrency tokens declared by participating
descendants. Scalar projections cast to the declaring subtype and guard its
discriminator, rather than loading payload or assigning a sibling's token.
The same projection primitive supports native refresh, explicit mutation, and
detached insertion. Tracker refresh preserves pending payload and original-value
semantics when SaveChanges uses `acceptAllChangesOnSuccess: false`.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx -c Release --filter "FullyQualifiedName~Ordering|FullyQualifiedName~SaveChanges|FullyQualifiedName~PostCommitOutcomeTests"` and expect all selected tests to pass.
- Require equal sort keys, database collation, manual flexible placement, rename reorder, callback-introduced changes, rollback restoration, and `SaveChangesAsync(false, cancellationToken)` coverage. Expect unsupported synchronous ordered saves to reject before partial structural work.
- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx -c Release --filter "FullyQualifiedName~ManagedSaveCallbackTests|FullyQualifiedName~SingleInsertGuardTests|FullyQualifiedName~OptionsExtensionTests"` and expect the persistence boundary on every provider, in both extension orders, with post-save inspection, audit writes, suppression rejection, and restored accepted callback writes after rollback.
- Run `OrderingTrackerTests.LateFailureRestoresAddedIdentityAndAllowsRetry` and `ManagedSaveCallbackTests.RolledBackCallbackWritesCanBeSavedAgain` on all five providers. The first covers a temporary key after a coordinated save fails; the second must persist restored Added, Modified, and Deleted callback entries after an insertion fails.
- Run `ManagedHierarchyPayloadTests` on all five providers. Payload writes to an unaffected tree must commit or restore with the insertion, while a structural write must fail before persistence.
- Run the mixed TPH/TPT managed-save regressions on every provider. Cross-type
  Parent changes, dependent cross-tree moves, base ordering, tracked refresh,
  both acceptance modes, and rejected or failed saves must preserve the full
  configured hierarchy rather than only the changed concrete subtype.

## Pros and Cons of the Options

### Database ordering with explicit asynchronous save integration

- Good, because the database compares mapped values and coordinated saves keep payload and hierarchy changes atomic.
- Bad, because the coordinator needs locks, tracker snapshots, refresh queries, and additional writes for affected intervals.

### Read-time sorting in application memory

- Good, because persisted hierarchy operations remain simpler and display order can vary per caller.
- Bad, because the application must load enough hierarchy to reconstruct traversal, and partial filtered results may not contain the required parents.

### Explicit reorder calls after every payload change

- Good, because structural changes remain visible in application code and a specialized caller can batch them.
- Bad, because callers can forget the reorder or commit a renamed node with stale structural order.

## More Information

A global OrderBy on a flattened result cannot preserve parent-before-descendant traversal. The implementation
maintains sibling positions and bounds, then `InTree(...).Nodes` returns preorder. Models without ordering retain
the ordinary EF save path. The record does not claim an interceptor alone can supply the full save lifecycle.

The coordinated save snapshots every tracked entry when hierarchy candidates are present. EF saves the context's
pending entries together, and save callbacks can change entries that were unchanged when planning began. A later
hierarchy failure must restore their original tracker state as well as the database transaction. Selecting only the
currently affected trees would therefore lose unrelated pending or callback changes. This cost is explicit and
bounded by the [tracker allocation regression](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackerSnapshotAllocationTests.cs);
applications should keep the context's tracked graph small for latency-sensitive hierarchy saves. See the
[performance contract](../../docs/performance.md) for the measured threshold.

Library-owned insertions save through EF's ordinary pipeline, so application save callbacks still run.
`UseNestedSets()` replaces the provider's standard `RelationalDatabase` service with a subclass. EF calls that
service once per save with the exact pending write set, after every `SavingChanges` callback and final change
detection and before the first command. The subclass validates the planned hierarchy entries there, admits ordinary
audit or outbox writes, and snapshots only those writes. EF accepts them with the insertion, so a later rollback in
the same insertion restores their pending tracker state from that snapshot. Post-save callbacks and tracker
inspection after the save are never validated against the plan. Registration rejects a provider-specific
`IDatabase` implementation instead of replacing it; SQL Server, SQLite, Npgsql, and Doka MySQL register the
standard service. The boundary adds no per-command interception; saves outside a managed insertion pay one
weak-table lookup.

For library-owned insertions, the managed save admits payload-only changes on already-tracked nodes in unaffected
trees, using the same structural and ordering property classification as coordinated saves. It rejects additions,
deletions, and changes to hierarchy-owned or configured ordering properties outside the planned insertion before
persistence begins. Every insertion uses an exact TreeId; there is no alternate scope-wide mutation surface.

EF keeps a generated temporary key in a tracker sidecar while the entity's CLR key remains at its sentinel.
After EF accepts an insertion, restoring both values requires the EF temporary-value setter: the public
`PropertyEntry.IsTemporary` setter copies the current value into the CLR member. The snapshot resolves the EF
setter once, only on rollback, and the generated-key callback tests exercise restoration and a later save on
all supported providers. This internal EF contract must be checked when upgrading EF Core.

### Re-evaluation Triggers

- A supported provider cannot translate a configured selector with the documented equality and ordering behavior.
- Measured refresh or snapshot cost exceeds an agreed workload budget.
- EF changes SaveChanges callback, acceptance, or generated-value semantics.
- EF changes inherited query discrimination, entity assignability, or the
  metadata identity of a configured hierarchy owner.
- EF changes its temporary-value sidecar setter or the behavior of `PropertyEntry.IsTemporary`.
- A supported provider registers its own `IDatabase` implementation or EF changes when it calls that service.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.
- 2026-09-24: Managed insertions validate EF's final write set at the relational database service, allow ordinary callback writes, and restore their accepted state after rollback.
- 2026-09-26: D-012 removes the retained scope-wide compatibility surface without changing coordinated save ownership.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed coordinated asynchronous saves, ordering refresh, callback write recovery, and rollback regression specifications against the linked repository evidence.
- 2026-10-03: Coordinated saves were bound to the cached configured owner after reproducing mixed-subtype Parent resolution and base-order selector failures.
- 2026-10-03: Derived-only generated tokens were included in owner-level scalar refresh after a subsequent payload save reproduced stale rowversion state.

### Implementation References

- [Save coordinator](../../src/Doka.EntityFrameworkCore.NestedSet/Features/ManagedSave/NestedSetSaveChanges.cs)
- [Persistence boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetRelationalDatabase.cs)
- [Managed save callback regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/SaveChanges/ManagedSaveCallbackTests.cs)
- [Managed hierarchy payload regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/ManagedHierarchyPayloadTests.cs)
- [Mapped ordering](../../src/Doka.EntityFrameworkCore.NestedSet/Mapping/NestedSetOrdering.cs)
- [Ordering components](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Ordering)
- [Ordering regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering)
- [Save regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/SaveChanges)
- [Mixed inheritance managed-save regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/Inheritance/InheritanceTests.ManagedSave.cs)
- [Derived rowversion regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Ordering/Tracking/RowVersion/DerivedRowVersionTests.cs)
- [Subtype-aware scalar refresh](../../src/Doka.EntityFrameworkCore.NestedSet/Features/ManagedSave/NestedSetRefreshProperties.cs)
- [Tracker snapshot allocation regression](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackerSnapshotAllocationTests.cs)
- [Ordering contract](../../docs/ordering.md)

### Sources

- [EF Core 10.0.12 inherited query scope](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.CreateSelect.cs) (primary source; retrieved 2026-10-03)
- [EF Core SaveChanges](https://learn.microsoft.com/en-us/ef/core/saving/) (primary source; retrieved 2026-09-23)
- [EF Core save events](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/events) (primary source; retrieved 2026-09-23)
- [EF Core tracked entry state](https://learn.microsoft.com/en-us/ef/core/change-tracking/entity-entries) (primary source; retrieved 2026-09-23)
- [EF Core temporary values](https://learn.microsoft.com/en-us/ef/core/change-tracking/miscellaneous#temporary-values) (primary source; retrieved 2026-09-24)
- [EF Core PropertyEntry.IsTemporary](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/ChangeTracking/PropertyEntry.cs) (primary source; retrieved 2026-09-24)
- [EF Core temporary sidecar implementation](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs) (primary source; retrieved 2026-09-24)
- [EF Core DbContext save sequence](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/DbContext.cs) (primary source; retrieved 2026-09-24)
- [EF Core RelationalDatabase](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Storage/RelationalDatabase.cs) (primary source; retrieved 2026-09-24)
- [EF Core relational service registration](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Infrastructure/EntityFrameworkRelationalServicesBuilder.cs) (primary source; retrieved 2026-09-24)
