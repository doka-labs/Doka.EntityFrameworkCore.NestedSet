---
id: D-011
status: implemented
date: 2026-09-25
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Internal source ownership and test layout in the EF Core package"
supersedes: []
superseded-by: []
amends: []
amended-by: [D-012]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-011 -- Group hierarchy behavior by feature

## Context and Problem Statement

The EF Core implementation placed related insert, move, delete, bulk import,
ordering, and maintenance code across `Operations`, `Querying`, `Ordering`,
`Validation`, and `Execution`. Finding the complete path for one operation
required crossing several folders. `NestedSetStore` mixed reads and writes in
one source file; `NestedSetSaveGroup` mixed lock planning, parent moves, order
changes, and refresh logic. The internal `INestedSetService<TEntity, TKey,
TScope>` had one implementation and no consumers. Its mandatory type parameter
also obscured the public distinction between scoped and unscoped trees.

The question is how to make feature ownership visible without introducing
another package boundary, duplicating the relational substrate, or changing
the public API and mutation behavior.

## Decision Drivers

- Make each mutation path discoverable and maintainable in one feature area.
- Preserve the existing public entry points, namespaces, model configuration,
  SQL behavior, allocation profile, and transaction boundaries.
- Keep shared relational mapping, storage, locks, and provider capabilities in
  one place rather than copying them into feature folders.
- Keep tests organized by behavior while retaining cross-feature qualification
  for providers, model shapes, transactions, and concurrency.

## Considered Options

- Feature-oriented folders inside the existing EF Core project
- Keep the horizontal folders and split only large classes
- Create a separate project or handler layer for each operation

## Decision Outcome

Chosen option: "Feature-oriented folders inside the existing EF Core project",
because it places operation-specific code together while keeping the common
database substrate shared. `Features` owns insert, move, delete, bulk import,
queries, ordering, managed SaveChanges, maintenance, and facade behavior.
`Mapping`, `Storage`, `Execution`, `Providers`, `Configuration`, `Diagnostics`,
and `Errors` remain shared. The public types keep their existing namespace even
when their source file moves. `NestedSetStore`, `NestedSetSaveGroup`, and
`NestedSetBulkPlan` are split into cohesive partial files so no new runtime
object is required. The unused internal service interface is removed; the
public scoped and unscoped API remains unchanged.

Internal feature namespaces follow their consuming feature. Tracked structure
refresh belongs to `ManagedSave`, callback write capture to `BulkImport`, and
generated concurrency refresh to `Insert`. `Features.Facade` owns model-based
typed dispatch into the consuming feature, without an internal forwarding
service; [D-012](D-012-typed-tree-runtime.md) records that runtime refinement.
This does not change any public facade namespace.

### Consequences

- Good, because a feature's algorithm and related data flow are easier to
  locate, and the old empty abstraction no longer implies mandatory scope.
- Good, because the refactor adds no dependency, dispatch object, project,
  query layer, or runtime allocation to the mutation path.
- Bad, because the shared substrate still requires navigation across folders
  when tracing a complete database operation. The boundary must remain explicit
  to prevent feature-specific logic from drifting back into shared files.
- Bad, because moving internal types changes their full names; source consumers
  within the repository must update their imports and tests.

### Confirmation

Live-provider cases become executable with each owning provider project. A
source/unit-only run does not qualify database behavior.

- Build `Doka.EntityFrameworkCore.NestedSet.slnx` in Release with warnings as
  errors and expect no compiler or analyzer diagnostics.
- Run feature and provider integration tests and expect unchanged hierarchy,
  ordering, transaction, and rollback behavior on every supported engine.
- Compare the public API baseline before and after the refactor; no public
  symbol or namespace may change.
- Search the source and documentation for the removed interface and old folder
  paths; neither may have an active reference.
- Validate this ADR and all local Markdown links after the file moves.

## Pros and Cons of the Options

### Feature-oriented folders inside the existing EF Core project

- Good, because operation ownership is visible without a runtime abstraction.
- Bad, because shared storage and transaction services remain outside each
  feature and must be followed when debugging an end-to-end operation.

### Keep the horizontal folders and split only large classes

- Good, because it minimizes source-path changes.
- Bad, because a feature remains scattered across unrelated technical areas.

### Create a separate project or handler layer for each operation

- Good, because project references could enforce feature boundaries.
- Bad, because mutually shared EF metadata, transactions, and storage would
  demand additional interfaces and dependency wiring without an independent
  deployment or versioning need.

## More Information

The root namespace and public entry points remain stable. The generic
`NestedSetNoScope` marker remains an internal implementation detail for trees
without a configured scope. No scope property is required of an unscoped
entity. The folder structure is not a new public contract.

### Re-evaluation Triggers

- A feature develops an independently versioned or deployable contract that
  justifies a project boundary.
- Shared files acquire feature-specific branches that make ownership unclear;
  relocate that behavior to its consuming feature and verify the call path.
- A future internal interface gains at least two real consumers with a
  behaviorally distinct implementation need.

### Decision History

- 2026-09-25: Decision recorded with status proposed.
- 2026-09-25: The owner requested the feature-oriented refactor; the exact decision text remains proposed until accepted.
- 2026-09-25: Source organization and the unused-interface removal were implemented without changing the public entry points.
- 2026-09-26: Single-feature execution helpers and the internal facade namespace were aligned with their actual consumers after review.
- 2026-09-26: D-012 replaces the retained internal service with typed feature dispatch and mandatory exact-tree storage.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed feature-owned source folders, shared execution and storage boundaries, the removed compatibility interface, and architectural regression specifications against the linked repository evidence.

### Implementation References

- [Source layout](../implementation-design.md#source-layout)
- [Bulk import](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetBulkInsert.cs)
- [Bulk plan staging](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetBulkPlan.Stage.cs)
- [Managed save](../../src/Doka.EntityFrameworkCore.NestedSet/Features/ManagedSave/NestedSetSaveGroup.cs)
- [Shared store](../../src/Doka.EntityFrameworkCore.NestedSet/Storage/NestedSetStore.cs)
- [Public unscoped entry point](../../src/Doka.EntityFrameworkCore.NestedSet/NestedSet.cs)
- [Public scoped entry point](../../src/Doka.EntityFrameworkCore.NestedSet/ScopedNestedSet.cs)

### Sources

- No external sources; repository evidence only.
