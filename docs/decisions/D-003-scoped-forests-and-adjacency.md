---
id: D-003
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Hierarchy identity, structural roles, tree selection, and optional Scope boundaries"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-003 -- Identify each tree with a stable TreeId and optional Scope

## Context and Problem Statement

Folders, KPI trees, organizational units, and group hierarchies need several
independent trees in one entity table. Bounds are local coordinates and may be
identical in different trees. A tenant or volume is a separate application
partition and must not be overloaded as tree identity. Queries from an
arbitrary node must resolve the containing tree without loading that node first.

## Decision Drivers

- Distinguish several trees inside one tenant, project, or volume.
- Keep every structural query and write inside a complete database predicate.
- Preserve a stable tree identity while bounds and root nodes change.
- Support repair from explicit Parent and sibling Position.
- Keep Scope optional for applications that do not have a separate partition.

## Considered Options

- Stable TreeId plus optional Scope and persisted adjacency
- Treat Scope as the only coordinate partition
- Derive tree identity from the root NodeKey
- Store adjacency only and use recursive reads

## Decision Outcome

Chosen option: "Stable TreeId plus optional Scope and persisted adjacency",
because it separates application partition, tree membership, node identity, and
coordinates. `TreeId` identifies a tree when Scope is absent; `(Scope, TreeId)`
identifies it when Scope is configured. Parent and Position remain the repairable
source for Depth and bounds.

### Consequences

- Good, because equal TreeIds in separate Scopes and equal bounds in separate trees cannot mix when every predicate uses the complete identity.
- Good, because `TreeContaining(nodeKey)` and other anchor queries resolve TreeId and bounds in one SQL expression.
- Bad, because a cross-tree move must update TreeId across the complete subtree and lock both tree identities.
- Bad, because applications must select and authorize Scope and TreeId rather than treating bounds as identity.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx --filter "FullyQualifiedName~NestedSetFacadeTests|FullyQualifiedName~NestedSetMutationFacadeTests|FullyQualifiedName~NestedSetMutationDeleteTreeTests"` and expect one-command anchor queries, query-filter composition, explicit TreeIds, cross-tree isolation, rejected cross-Scope moves, and tombstone coverage to pass.
- Run `dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests.csproj` and expect every provider migration to contain the typed registry plus indexes beginning with Scope when configured and TreeId in every hierarchy.

## Pros and Cons of the Options

### Stable TreeId plus optional Scope and persisted adjacency

- Good, because identity remains explicit from an arbitrary node and unrelated trees use independent coordinate spaces and locks.
- Bad, because the model needs one additional stable scalar role and registry lifecycle.

### Treat Scope as the only coordinate partition

- Good, because a model with exactly one tree per tenant uses one fewer property.
- Bad, because several trees in one tenant share identity and writer coordination, and Scope cannot simultaneously represent tenant and tree.

### Derive tree identity from the root NodeKey

- Good, because the root already has a stable node identity.
- Bad, because every arbitrary-node operation must discover the root, and moving or replacing a root changes an identity that applications may persist externally.

### Store adjacency only and use recursive reads

- Good, because a Parent change updates fewer database rows.
- Bad, because ordered ancestor and subtree reads become provider-specific recursive queries and no longer implement the package's Nested Set contract.

## More Information

One TreeId has exactly one root. Root Parent is null, Depth and Position are
zero, Left is one, and Right is twice the node count. NodeKey is a stable scalar
primary or alternate key. With Scope, `(Scope, NodeKey)` is unique and forms the
self-reference; TreeId is excluded so a subtree can change trees without a
self-referential cascading key update. Deleting or merging a tree leaves its
identity tombstoned so an accidental reuse cannot attach a new generation to
stale application references. `PurgeTreeIdAsync` is the explicit administrative
escape hatch: it locks the exact typed identity, accepts only a tombstone, proves
that no hierarchy rows remain, and then deletes exactly that registry row.
Authorization and audit policy for this destructive identity-lifecycle step
remain application responsibilities.

### Re-evaluation Triggers

- A supported provider cannot store, compare, parameterize, or index a required TreeId type.
- A measured workload is dominated by writes to one hot tree and should use another hierarchy model.
- EF Core adds a relational hierarchy primitive that preserves the same public identity and query contract more efficiently.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: The proposal recorded the earlier Scope-only implementation for retrospective comparison.
- 2026-09-23: Stable TreeId, optional Scope, one root per tree, and persisted adjacency were confirmed against the target architecture and implementation.
- 2026-09-23: Status changed from proposed to accepted.

- 2026-09-28: The maintainer confirmed acceptance and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed stable typed TreeId, optional Scope, persisted adjacency, registry lifecycle, and facade regression specifications against the linked repository evidence.

### Implementation References

- [Hierarchy facade](../../src/Doka.EntityFrameworkCore.NestedSet/NestedSet.cs)
- [Scoped facade](../../src/Doka.EntityFrameworkCore.NestedSet/ScopedNestedSet.cs)
- [Typed tree registry](../../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetInfrastructureConvention.cs)
- [Tree identity purge](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Maintenance/NestedSetTreeRegistryPurger.cs)
- [Facade query tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Facade)
- [Facade mutation tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade)
- [Tree identity purge tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Purge.cs)
- [Hierarchy contract](../../docs/hierarchy-model.md)

### Sources

- [EF Core keys](https://learn.microsoft.com/en-us/ef/core/modeling/keys) (primary source; retrieved 2026-09-23)
- [EF Core foreign and principal keys](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/foreign-and-principal-keys) (primary source; retrieved 2026-09-23)
- [EF Core global query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters) (primary source; retrieved 2026-09-23)
- [MySQL foreign-key differences](https://dev.mysql.com/doc/refman/8.4/en/ansi-diff-foreign-keys.html) (primary source; retrieved 2026-09-23)
