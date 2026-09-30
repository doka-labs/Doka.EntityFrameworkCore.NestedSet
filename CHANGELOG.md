# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and released versions
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 10.0.0-dev (unreleased)

Initial development line. This version is not published.

### Breaking changes during development

- Hierarchy insertion and deletion require the `NestedSet<TEntity>` facade;
  direct `DbSet<TEntity>.Add` and `Remove` do not coordinate bounds, registry
  state, or tree locks and are rejected by the save guard.
- Context options must call `UseNestedSets()` to install the model conventions,
  provider capabilities, save guard, and persistence boundary. Mapping alone is
  not sufficient. A provider-specific `IDatabase` service is rejected.
- Registry table names now derive from physical hierarchy identifiers instead
  of the CLR entity name. Existing development databases using the earlier
  naming rule need a data-preserving registry table rename; see
  [migration guidance](docs/migrations.md).
- SQL Server caller transactions with `XACT_ABORT ON` now fail before a
  hierarchy write with `InvalidTransaction`. Use `SET XACT_ABORT OFF` when the
  library must preserve earlier caller work through a savepoint.

### Added

- Add the EF-independent `Doka.NestedSet` package with
  `INestedSetNode<TNodeKey, TTreeId>`, its scoped counterpart, validated bounds,
  and in-memory ancestor/descendant predicates.
- Add `Doka.EntityFrameworkCore.NestedSet` with fluent mapping for NodeKey,
  stable TreeId, optional Scope, Parent, bounds, depth, and dense sibling
  position on existing entities.
- Add one root per TreeId, independent coordinates across trees, complete-tree
  lookup from any node, and composable tree, subtree, child, descendant,
  ancestor, and parent queries.
- Add transactional root/child/before/after insertion, subtree movement,
  single-node deletion with child promotion, and subtree deletion.
- Add declarative ascending/descending compound sibling order, stable key tie
  breaking, strict placement, optional manual overrides, and automatic async
  `SaveChanges` reordering when an order field or parent changes.
- Add atomic forest and subtree import with assigned or generated keys,
  complete-input validation, rollback restoration, and one reserved interval.
- Add typed hierarchy failures, detailed validation issues, adjacency-based
  rebuild, activity/metric instrumentation, a context-bound hierarchy facade,
  and custom context save integration.
- Distinguish a concurrent pre-lock anchor tree change with
  `ConcurrentTreeIdentity` while keeping truly absent anchors as `NodeNotFound`.
- Add a typed per-tree registry for independent server-side tree locks and
  lifecycle state while retaining SQLite's database-writer boundary.
- Add ordinary EF migration and physical-index support for Doka MySQL/MariaDB,
  PostgreSQL, SQLite, and SQL Server.
- Add optional SafeMigrations 10.4.5 integration tests for MySQL/MariaDB,
  PostgreSQL, and SQLite without making SafeMigrations a runtime prerequisite.
- Add runnable folder, KPI, and tenant user-group samples, including role and
  privilege relationships owned by the application.

### Performance and reliability

- Use set-based scoped range updates, bounded structural projections, cached
  model metadata, iterative traversal, changed-interval refresh, and bounded
  rebuild batches.
- Preserve native provider key and collation equality for scoped and ordered
  queries, including compiled-model metadata.
- Keep converted Scope and TreeId identities separate in save planning;
  reject callback changes to stored Scope, TreeId, and Parent values during
  insertion, and verify imported NodeKeys by provider value during refresh.
- Recheck changed node tree identities under the coordinated save's locks and
  reuse its transaction for Parent moves without nested lock acquisition.
- Refresh tracked structure after ordered saves by the complete Scope and
  NodeKey identity, including converted keys and tracked scope aliases; resolve
  changed ordered identities in bounded key batches so large saves stay below
  provider parameter limits; and apply the locked-tree filter once per tracked
  capture rowset.
- Avoid redundant change detection and unrelated entity-entry allocations
  during individual hierarchy inserts while retaining callback guards.
- Validate managed insert plans once at EF's relational persistence step after
  every save callback, without per-command interception; allow ordinary audit
  and outbox writes in the same save; and restore their accepted tracker state
  when a later insertion step rolls back.
- Guard Parent and structural changes on derived TPH and TPT entity types
  through their base hierarchy mapping.
- Use a single JSON rowset parameter for wide multi-tree lock ordering, and
  retain bounded full validation samples with complete typed issue counts.
- Validate real provider query plans, structural command/update counts, affected
  rows, rollback, cancellation, concurrent writers, pooling, and deep-tree
  behavior across the supported matrix.
- Keep local benchmark timing and allocation observations outside CI and release
  acceptance because hosted runner hardware is not a deterministic baseline.

### Packaging and supply chain

- Add locked dependency graphs, warning-free Release builds, public API
  baselines, XML documentation, exact package/symbol inspection, isolated
  package consumers, and per-package SPDX 2.2 SBOM validation using the
  standalone Microsoft binary.
- Add full-SHA-pinned CI, dependency review, Scorecard, and release-candidate
  workflows with signed provenance, signed SBOM attestations, protected
  short-lived NuGet authentication, repository-signature verification, public
  package/symbol readback, and immutable GitHub release reconciliation.
- Add digest-pinned MySQL 8.4, MariaDB 11.8, PostgreSQL 17, and SQL Server 2025
  test images plus Dependabot updates for SDK, images, packages, and actions.

### Documentation and governance

- Add task-oriented product, configuration, ordering, migration, transaction,
  diagnostics, performance, deployment, security, and release documentation.
- Add a Doka MADR Enterprise Profile 1.0 decision corpus based on MADR 4.0,
  release governance, threat model, assurance case, OpenSSF evidence mapping,
  and hosted-settings runbook.
