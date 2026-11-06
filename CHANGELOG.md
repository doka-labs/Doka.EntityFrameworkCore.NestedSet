# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Version policy,
including documented exceptions to Semantic Versioning, is defined in
[Release governance](docs/release-governance.md#version-policy).

## 10.3.0 (2026-11-06)

Stable contract for all six packages on .NET 10 and EF Core 10. Update all
selected adapters and the common EF package together.

### Changed

- Refresh the published dependency graph to Doka 10.6.4 and SafeMigrations
  Core/all four adapters 10.7.0. Raise the MySql adapter's Doka floor to
  `[10.6.4,10.7.0)` while preserving the EF and database LTS lines.
- Require explicit `CancellationToken` arguments on owned asynchronous
  operations and helpers. This is source-breaking for calls that omitted them;
  see the [upgrade guide](docs/upgrading-provider-packages.md#upgrade-from-1020-to-1030).

### Fixed

- Restore the explicit backing fields and writable owned payload needed by EF
  regression fixtures after cleanup. Check exact field bindings, changed field
  identities and application payload across valid and rejected rollback paths.
  Restore the migration fixture's stable entity-name getter and cover the
  historical, current and specialized model identities without database I/O.
- Align the portable signed build-provenance filename with Doka and
  SafeMigrations (`release-provenance.intoto.jsonl`) so release consumers can
  discover it. Preserve signature verification and exact asset inventories;
  published releases retain their original assets and tagged verification guide.

## 10.2.0 (2026-11-03)

Stable contract for all six packages on .NET 10 and EF Core 10. This additive
release preserves the provider-callback registration introduced in `10.1.0`.
Update selected adapters and the common EF package together. Existing mappings
need no migration unless an application opts into an explicit registry name.
See the [upgrade guide](docs/upgrading-provider-packages.md#upgrade-from-1010-to-1020).

### Added

- Add `HasTreeRegistryTable(string)` to name one hierarchy's typed tree registry
  instead of the generated `DokaNestedSetTrees_<hash>`. Providers validate length
  and collisions during model finalization. Without the option nothing changes;
  naming an existing registry migrates as a data-preserving table rename.

### Changed

- Run pull-request and release-candidate qualification through the same parallel
  jobs, including package consumers and SBOMs, while retaining exact candidate
  sealing and protected publication.
- Standardize Unit test discovery, resource cleanup and contributor guidance,
  keeping timing benchmarks outside release gates.

### Fixed

- Handle delayed GitHub maintenance-run metadata and restrict dependency-audit
  uploads to their intended reports and logs.
- Complete saved insertion relationships with one traversal of the identity
  source, including single-use producers. Reuse the save guard's exact insertion
  sets and retain only relationship candidates, without a batch buffer on the
  no-relationship path. Remove a duplicate metadata snapshot and filtered
  source/target identity arrays without changing rollback or error precedence.
- Preserve disabled automatic change detection for ordinary saves, coordinated
  Parent/order changes, and single/bulk/forest facade insertion. Explicitly
  recorded hierarchy changes still coordinate atomically; unrecorded hierarchy
  edits reject instead of triggering context-wide detection. Read-only guards
  retain structural, ordering, navigation, locking, and rollback protection
  without discovering unrelated unmarked snapshot-tracked payload.
- Reject client-side cascading deletion of tracked hierarchy nodes through an
  ordinary principal before payload SQL. Hierarchy deletion must use the facade.
- Keep saves usable after a detached child is removed from a loaded collection,
  and permit replacement principal instances whose relevant FK values remain
  unchanged. Native reference lookups observe EF tracking without adding entries.
- Accept matching Parent references and inverse collections for explicitly marked
  Parent moves, including manual detection. Compare navigation effects with the
  planned destination and restore pending CLR graphs and installed relationship
  snapshots independently after failure.
- Keep prepared external-owner collections usable after successful facade
  insertion. Manual-detection imports may prepare the complete validated subtree
  or forest collection across batches; only the active batch may persist, and
  foreign or prematurely Added inputs remain rejected.
- Avoid rebuilding complete prepared-owner reference sets on each import wave.
  Reuse tracked-removal scratch and group saved-member collection scans per owner
  within the bounded wave, preserving live callback and FK validation.
- Baseline ordinary FK writes from independent pre-SQL values only
  after provider success. Unmarked payload and command/completion callback edits
  cannot authorize an unsaved relationship.
- Restore callback-selected relationships after coordinated rollback and keep
  actual persisted-FK semantics consistent across ordinary and coordinated saves.
  Accept marked Parent moves to tracked targets when the old parent is detached,
  retaining rejection of unplanned callback changes.
- Undo only library-installed native collection additions during coordinated
  rollback. Mixed raw and public FK writes in one callback no longer restore a
  post-callback owner graph as pre-save state. Preserve original memberships,
  installed dependent maps, original FK values and explicit modification flags.
- Preserve initially unchanged FK relationships when callbacks move native
  identities and raw-reset their scalars. Supplemental recovery keeps original
  native presence/absence and selected list occurrences without erasing unrelated
  pending CLR edits. Retire verified receipts for idempotent recovery and keep
  unexpected-Parent cleanup active for supplemental captures.
  Preserve exact native buckets across required-FK conceptual nulls and already
  divergent raw current values, including first-use native index initialization.
- Restore originally Unchanged table-sharing peers after EF promotes them to
  Modified without property flags. Failed OwnsOne and ordinary table-split saves
  retain their original exception and exact pending state, including identical
  retry. Restore supplemental native collection membership independently from
  an unmarked CLR reorder, even after undoing a saved-member receipt. Restrict
  collection-order reflection to collection navigations and extend complete FK
  capture and recovery resource controls to 20,000 members.
- Preserve raw complex references, collection instances and element states when
  recovering promoted table-sharing peers, including nullable complex-only
  elements and provider-created native entries for raw additions. Managed insert
  rollback also captures the unchanged same-row peers of callback writes before
  EF promotes them. Keep EF's native JSON cardinality semantics with manual
  detection; no context-wide detection or relaxed hierarchy guard is introduced.
- Restore captured complex containers before ordinal/scalar recovery when a
  save callback removes, clears, nulls, replaces or reorders them. Preserve caller
  instances and independent native current/original slots, including accepted
  lifecycle changes, pending additions/deletions and repeated failure. Keep
  manual detection and structural guards intact; allocate the topology journal
  only for entities with complex properties. Preserve scalar intent on native
  children hidden by raw-null/shortened parents and undo temporary original/live
  containers before restoring those absent paths.
- Recover composed nested callback changes and pending Added children without
  retaining temporary native lane lengths or holes. Preserve captured identities,
  original errors and later detection/retry; retire only post-capture cache
  occurrences when their CLR ancestor has disappeared.
- Prevent a process-terminating stack overflow when a failed save restores a
  pending Deleted complex element below which a callback changed the collection.
  Retire and reinstall native entries through separate live and captured access
  graphs, including pending Added children of a removed owner and whole-entity
  state transitions.
- Capture Deleted entities whose complex collection elements contain struct
  references. Restore mutable primitive values inside nullable struct references,
  and replay struct containers without consulting their caller-defined `Equals`.
  Staging field-mapped collections no longer calls caller collection code.
- Recover Deleted elements retained by a resurrected entity, scalars of raw CLR
  elements EF never registered, read-only views with changed backing lists,
  collections replaced behind copying setters and pending writes after a callback
  cleared the change tracker.
- Avoid unnecessary writes to unchanged read-only/immutable collections and
  copying property setters. Restore struct primitive collections through their
  EF comparer snapshots, including nested and hidden original elements, without
  replaying stale boxes over restored scalar values.
- Avoid transient identity maps for proven aligned complex lanes and skip
  recursion for empty nested metadata. Preserve the general divergent-lane
  protocol and unchanged complete capture/recovery allocation ceilings.

## 10.1.0 (2026-10-29)

Stable contract for all six NestedSet packages on .NET 10 and EF Core 10.
The provider-package and registration transition below is explicitly breaking;
follow the [upgrade guide](docs/upgrading-provider-packages.md) when updating
from `10.0.0`. The shipped API baselines record the `10.1.0` contract, while SDK
package validation retains the published `10.0.0` compatibility baseline and
the narrowly approved registration-removal exception.

### Added

- Support independently qualified framework release lines through the existing
  parallel six-package pipeline. Keep `main`/10 active and the registered
  maintenance/preview lines inactive; reuse protected publication, retain exact
  candidate bytes, promote stable Latest monotonically, and service active
  branches with their own audits, CI and dependency updates. See
  [release lines](docs/operations/release-lines.md).
- Compose multiple subtrees, strict ancestors and ancestors-or-self through the
  existing scoped, unscoped and exact-tree bindings. Preserve source occurrences,
  mapped distances, application filters and mixed-direction composition.
- Filter complete visible paths, any visible ancestor or the nearest candidate
  before its value/path checks, without preliminary entity loads or fallback to
  a farther matching owner.
- Opt into a generated nonleaf property and additive ancestor index with
  `HasAncestorIndex()`. Use eligible directly bound public Doka source-binding
  and ordered-join APIs through the `.MySql` adapter. See the
  [capability and workload table](docs/configuration.md#optional-ancestor-index).
- Report schema-only ancestor-index reads on adapters without an optimized
  reader through filterable EF diagnostics, once per actual context.
- Extend the independently synthetic SignalLab and UserGroups samples with
  connection availability, typed tags, event aggregation, global/membership roles, ambiguity evidence and
  bounded supervisor chains. Complete report/chain reads share one snapshot.

### Changed

- Use a self-contained fictional acquisition model in SignalLab, its tests,
  benchmarks and migrations. Normalize distinct pulse, sweep and burst event
  formats through explicit projections; retain independent event identity,
  path/owner semantics, streaming results and fixed allocation/work contracts.
  Historical measurements remain labeled with their original source scope.
- Split native integration into `.MySql`, `.Postgres`, `.Sqlite`, and `.SqlServer`
  shipping packages while retaining one common EF hierarchy engine. Each adapter
  owns its native SQL, model/index conventions, registry transport, and query
  integration through direct provider dependencies.
- **Breaking:** install the selected NestedSet adapter and register
  `UseNestedSets()` inside its native provider callback. Remove the former
  `DbContextOptionsBuilder.UseNestedSets()` calls. Model mappings and public
  hierarchy operations stay common. See the [upgrade guide](docs/upgrading-provider-packages.md)
  and [D-020](docs/decisions/D-020-provider-owned-runtime-packages.md).
- Bind each adapter to the exact common EF package version it was built with.
  Update NestedSet adapters together; their private integration contract is not
  a promise of compatibility with another kernel patch.
- Reuse request-free supervisor projections while preserving per-request
  authorization and deferred source values. Replace optional reflected Doka
  feature discovery and its options/assembly lookup caches with the MySql
  adapter's directly typed Doka 10.6.2 APIs and exact method identities.
- Refresh the qualification graph to published Doka 10.6.3 and SafeMigrations
  10.6.1 and EF Core 10.0.14. Preserve shipping
  EF compatibility ranges and database LTS lines. The newer local adapter
  contract, dated local evidence and fresh published-graph qualification are explicit in the
  [qualification boundary](docs/qualification/provider-migrations.md).
- Refresh the existing MySQL 8.4.13 and MariaDB 11.8.9 image digests from their
  official registries. Pin CodeQL SARIF uploader 4.39.1 to its release commit
  rather than the annotated tag object. Retain dated runtime evidence separately
  from the updated package and image declarations.
- Update property testing to FsCheck 3.6.0, Test SDK and Code Coverage to
  18.11.0, and the qualification-only PKCS dependency to 10.0.14 for .NET 10.
- Refresh MySQL to 8.4.13 and SQL Server 2022 to CU27-GDR1; refresh the existing
  MariaDB 11.8.9 and PostgreSQL 17.11 image digests. Keep the supported engine
  lines and update the CodeQL SARIF uploader to 4.39.1.
- Tag public composition queries on SQL Server and plan only those commands with
  `OPTION (OPTIMIZE FOR UNKNOWN, MAXDOP 1)` through an adapter-owned command
  interceptor. In the measured capacity profiles, plans cached for one selected
  source or rebuilt per worker thread no longer repeated population work for
  later sources; neither hint fixes a physical plan. Ordinary
  hierarchy reads, mutations and other adapters are unchanged. See
  [SQL Server composition plans](docs/performance.md#sql-server-composition-plans).
- Declare the SQL Server adapter's PKCS security floor as `9.0.21` without an
  arbitrary upper bound, allowing applications to use newer compatible framework
  packages. The common libraries retain no SQL Client dependency.

### Fixed

- Fence SQL Server's row-independent long-path failures behind an `OFFSET 0`
  boundary and test each path through a scalar `TOP(1)` lookup,
  and separate unresolved supervisor markers from complete
  unique-person evidence. The UserGroups sample fences its supervisor
  witnesses behind `OFFSET 0` on SQL Server. For single-group rules it groups
  each origin's marker with its witness occurrences instead of joining origins
  to witnesses. Guard actual iterator
  output and pre-filter reads against cardinality-derived budgets; retain native
  x64 qualification.
- Project SQL Server's nearest decision once in equivalent descending coordinate
  order. Refresh fixture statistics individually with unchanged `FULLSCAN` accuracy and deadlines;
  retain the explicit native capacity qualification boundary.
- Drive SQL Server's strict ancestors from visible nonleaf owners into one
  fenced `Left` range each, and fence independent fact
  selections by node key before inclusive ancestor ranges. Preserve owned
  auto-includes, collection DTO identifiers and equal-bound owner occurrences
  without a global owner window.
- Select PostgreSQL's nearest-owner decision once while preserving `Depth`
  ordering, rejection without fallback and complete-path validation. Guard
  broad payload joins with actual native work to prevent quadratic rescans.
- Include the existing Npgsql provider's PostgreSQL SPDX license in dependency
  review while retaining vulnerability checks and exact package exceptions.
- Keep exact-candidate consumer inputs as project templates so automatic NuGet
  dependency submission restores only runnable projects. Materialize the same
  reviewed projects for isolated package qualification without weakening
  candidate-version, dependency-closure or archive-byte checks.
- Preserve portable complete-path proofs through exact visible parent existence
  per child.
- Correct shared capacity setup at SQL Server's default compatibility level,
  batch large payload seeds by primary key, and enable snapshot isolation in
  indexed SignalLab fixtures. Check cancellation forwarding at the command and
  reader boundaries rather than requiring a driver-specific exception token.
- Separate instrumented plan replay from ordinary query deadlines, reject
  interrupted MySQL plans, and retain failed deep-query SQL and estimated plans.
  Result, allocation and native-work assertions remain unchanged. See the
  [CI correction evidence](docs/qualification/query-composition.md#ci-correction-qualification-2026-10-28)
  for executed checks and remaining native-platform limits.
- Bound eligible strict ancestor owners by a shared selected source and the
  existing structural index, without requiring generated ancestor schema.
  Complete matched identities before payload hydration on known MySQL for
  repeated direction changes; retain source-only binding on MariaDB.
  Preserve the complete walk bag through the required public provider APIs.
  Preserve portable strategies for ineligible sources, model filters, exact identities,
  containment, and mapped endpoint checks.
- Keep supervisor source selection selective through necessary membership
  witnesses, then rejoin the original authorized users and groups before
  candidate decisions. Preserve membership validity, visibility, source status,
  and occurrence evidence.
- Count supplementary characters correctly for known Doka/Npgsql character
  mappings after conversion, and reject malformed surrogate sequences before
  registry commands. Preserve binary byte limits and conservative custom mappings.
- Recognize assigned property-bag keys using their raw EF sentinel. Failed single
  and bulk insertions restore absent dictionary keys separately from present
  null/default values while retaining unrelated application payload.
- Preserve overflow-safe nonleaf classification at `Int64.MaxValue`, including
  representable malformed split intervals, without changing mutation arithmetic.

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
- Add runnable hierarchy, aggregation, and tenant user-group samples, including role and
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
