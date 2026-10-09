# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and released versions
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## 10.0.0 (2026-10-09)

First stable contract for `Doka.NestedSet` and
`Doka.EntityFrameworkCore.NestedSet`, targeting .NET 10 and EF Core 10.
It includes the hierarchy, query, mutation, ordering, bulk-import, maintenance,
and diagnostics APIs introduced in `10.0.0-rc.1`, with the corrections below.
Both packages share the same stable version and reviewed API contract.

### Fixed

- Observe asynchronous server-session removal in the MySQL/MariaDB fixture
  lifecycle tests while proving foreign idle-pool reuse.
- Seed ordering-refresh fixtures in bounded native batches inside one
  transaction, preserving mapped values and rollback without tracking every
  child before the measured operation.
- Coordinate mixed TPH/TPT parent and ordering changes through the configured
  hierarchy owner, including sibling derived types and tracked descendants.
- Preserve principal database collation for converted string-backed NodeKeys
  when resolving Parent links in queries and structural operations.
- Project absent nullable Parents without materializing a converted default
  value key from SQL NULL, including custom value-type keys at tree roots.
- Keep SQLite repair and bulk-finalization batches on unique-key lookups
  without requiring manually collected optimizer statistics, while retaining
  exact Scope and TreeId membership checks.
- Specialize untouched PostgreSQL indexes with TreeId and nullable-Parent
  predicates so FK principal checks cannot scan generated scope-leading tree
  paths during large atomic imports. Preserve application-owned metadata;
  review scaffolded index rebuilds as described in the migration guide.
- Refresh generated concurrency tokens declared only on derived types for
  coordinated saves, structural mutations, and base-facade single/bulk inputs.
- Avoid retaining detached EF entries during bulk final refresh and store
  native rollback coordinates without per-coordinate boxes.
- Preserve never-staged generated input values after early bulk failure and
  reject callback changes to assigned import keys before they replace rollback
  identity snapshots.
- Reject assigned and generated single-insert key changes before callbacks can
  hide them through EF acceptance. Restore failed insertion graphs and release
  their exact native reference, identity, and dependent-map memberships without
  clearing unrelated application tracking.
- Complete single-insert detachment before commit or caller savepoint release,
  restoring generated root and owned payload values, owned keys, and ownership
  foreign keys after failure while preserving ordinary payload and business
  foreign-key edits.
- Validate insertion's EF reflection contract once at `UseNestedSets()`
  registration, reporting incompatible framework members before a context
  performs writes.
- Reuse unchanged scalar insertion identity snapshots and foreign-key vectors
  through typed comparisons, retaining mutable-key snapshots and every
  callback-boundary verification.
- Extend typed insertion comparisons to identity matching and detach preparation,
  preserving installed relationship snapshots and comparer null semantics.
  Allocate dependent-bucket storage only when used, and validate exact framework
  signatures at registration without compiling unused probe operations.
- Share tracked structural/token refresh through the exact EF entry identity
  without redundant key arrays or unused update adapters.
- Avoid duplicate forest-wide identity indexes for a single imported tree.
  Keep cross-tree reference/key rejection before database work and release its
  validation indexes before entering the asynchronous mutation boundary.

### Changed

- Establish the reviewed public API contract in both shipped baselines and use
  `10.0.0` as the shared package version without a prerelease suffix.
- Upgrade optional SafeMigrations qualification adapters from 10.4.5 to
  10.4.9. Verify PostgreSQL's canonical Parent and TreeId null-filter indexes
  through creation, preflight, replay, catalog comparison, and drift rejection
  without changing the regression expectations or ordinary migration path.

### Added

- Add FsCheck properties for Int64 bounds, complete tree identity, generated
  bulk geometry and sibling ordering, and malformed-import rejection. Keep
  shrinkable input and replay evidence in the existing unit test projects.
- Review the two native Microsoft license files used by optional SQL Server
  qualification through targeted Dependency Review exceptions, restricted to
  SNI.runtime 6.0.2 and NativeInterop 0.20.6 by the inline version check.

- Add optional SQL Server SafeMigrations 10.4.9 consumer integration for `dbo`
  and explicit schemas, generated tooling registration, index apply/replay and
  drift rejection, and CHECK creation, trust, and data-proof boundaries. Verify
  generated Int32-to-Int64 upgrades and valid populated integer CHECKs, reject
  invalid rows with complete upgrade rollback, and preserve Int64 schemas on
  refused narrowing even when rows fit Int32. Ordinary EF migrations and shipping
  dependencies remain unchanged.
- Add real relational capacity cases for ten million stored nodes, one million
  direct children, Depth 100,000, million-node import/rebuild memory and late
  rollback, plus 64 independent-tree and hot-tree writers.

## 10.0.0-rc.1 (2026-10-01)

First release candidate of `Doka.NestedSet` and
`Doka.EntityFrameworkCore.NestedSet` for .NET 10 and EF Core 10. This prerelease
precedes the initial stable `10.0.0`; no stable compatibility baseline has been
published yet.

### Changes from development builds

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
  deterministic validation/indexing, release governance, threat model,
  assurance case, OpenSSF evidence mapping, and hosted-settings runbook.
- Prepare the 10.0.0-rc.1 public API declarations in the unshipped baselines and
  complete Passing evidence for OpenSSF project 15143, including
  secure-development guidance and measured branch coverage. The badge remains
  a self-assessment, not a certification.
