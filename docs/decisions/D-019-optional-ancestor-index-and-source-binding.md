---
id: D-019
status: implemented
date: 2026-10-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Optional generated nonleaf indexes and exact public Doka source binding for selected ancestor queries"
supersedes: []
superseded-by: []
amends: []
amended-by: [D-020]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-019 -- Opt into generated ancestor indexes and statement-local source binding

## Context and Problem Statement

[D-020](D-020-provider-owned-runtime-packages.md) amends the provider-binding
and installation assumptions below: the MySql adapter now has a direct Doka
dependency and typed public binding. The historical experiments and optional
ancestor-index semantics in this record remain unchanged.

The generic composition contract in
[D-018](D-018-generic-hierarchy-query-composition.md) preserves exact source
occurrences, visible mapped owners, and both-direction composition. Its
existing Doka ancestor strategy can still visit a broad owner population for a
selective source. A LATERAL spelling alone did not remove that work. Repeating
a selected source also cannot promise stable occurrences when paging or
volatile projected values are involved.

Native experiments identified two separate capabilities: an indexed generated
nonleaf flag and a provider-owned statement-local source binding. Neither
replaces the exact containment and tree-identity predicates. An additional
index also has storage and coordinate-write costs, so it must not become an
unconditional schema requirement for every hierarchy.

## Decision Drivers

- Preserve strict and inclusive containment, source bags, exact mapping, and visibility.
- Keep deferred EF execution, one statement per attempt, cancellation, and retries.
- Derive index metadata from completed physical structural roles.
- Keep ordinary migrations independent of optional SafeMigrations adapters.
- Preserve the existing model without the option and corresponding query fallbacks when required provider features are absent.
- Avoid private provider reflection, context retention, and a shipping Doka dependency.
- Retain native plan, allocation, write, and storage evidence without universal optimizer claims.

## Considered Options

- Explicit generated ancestor index with an optional public provider source binding
- Add the ancestor index and source boundary to every hierarchy
- Retain the current schema and change only the correlated SQL spelling

## Decision Outcome

Chosen option: "Explicit generated ancestor index with an optional public provider source binding", because
the observed read opportunity has a concrete schema and persistence cost that
applications must choose, while stable statement-local source occurrences
belong to the query provider rather than consumer raw SQL.

The existing source-binding capability is also reusable by eligible strict
ancestor queries without generated ancestor schema. That ordinary-model route
completes only selected structural coordinates, derives source envelopes per
Scope and tree, and range-checks nonleaf owners through the existing Right
index. Model visibility, every original containment comparison, and final
mapped-alias checks remain authoritative. Fact-bearing selections keep their
own narrow structural `DISTINCT` boundary; consumer bags are not deduplicated.
The ordinary route additionally requires the existing ordered-join capability.
Only positively identified MySQL receives a nested source binding over matched
key pairs without `DISTINCT` before payload hydration. Its recursive completion
fence separates repeated direction changes from one merged hydration/EXISTS
optimizer search. MariaDB and unknown engine identification retain source-only
binding. Source hydration stays ordinary and only final owner hydration receives
a directed edge on both routes. Each binding
completes its own input once; separate composition calls remain separate inputs.
This may limit downstream predicate pushdown and adds an intermediate relation,
so a bounded owner range alone does not establish cheaper complete reads.
The all-engine pair boundary was rejected after the recorded MariaDB
branched-chain sample exhausted its container memory. The existing public
engine-identification API suffices; no additional Doka feature is needed.
Hierarchy-correlated selections or missing required capabilities retain the
established independent-coordinate route. This adds no public API, index or
migration, and does not change the separate generated-index/inclusive contract.

`HasAncestorIndex(string nonLeafPropertyName = "NestedSetIsNonLeaf")` creates or
validates a required generated Boolean for mathematical `Right > Left + 1`,
guarded as `Right > CASE WHEN Left = 9223372036854775807 THEN Left ELSE Left + 1 END`
to preserve all signed storage values without overflowing the addition. The
same scalar guard serves the four existing nonleaf query gates; mutation
arithmetic is unchanged. The option creates an additive ascending
`(Scope?, flag, TreeId, Right, Left, NodeKey)` index. Duplicate mapped
properties are removed; existing structural and sibling-order indexes remain.
The generated property must be physically co-located with its boundary and
index columns. Compatible application metadata can be adopted, while
incompatible defaults, conversion, generation, or save behavior fail clearly.
The generated value participates in tracked structural refresh and is not
writable hierarchy structure.

The configuration callback records the property name. Property/index creation
runs in an early model finalizer, after application inheritance and fragment
choices but before provider index-key length budgeting. TPT can place the flag
with inherited structural bounds; a concrete TPC hierarchy must not introduce
it on unrelated sibling tables. Application configuration that refers to the
flag earlier declares its required Boolean explicitly first. Property bags
receive an indexer property with the natural missing-key sentinel rather than
requiring callers to initialize a generated dictionary entry. Conflicting
non-null indexer sentinels are rejected.

PostgreSQL 17 uses stored generation. Doka MySQL/MariaDB and SQLite use virtual
generation, and SQL Server uses a deterministic non-persisted Boolean
expression. The option changes the EF model, requires a migration, and requires
application-owned compiled models to be regenerated.
SQL Server's indexed-computed-column session settings remain an application
connection and deployment prerequisite; the library does not silently alter
session options.

Historical native qualification confirmed a published Doka 10.5.0 automatic string-length
budgeting defect on both MySQL and MariaDB. Implicit `utf8mb4` Scope, TreeId,
and NodeKey strings are each assigned 255 characters, consuming 3,060 index
bytes; the generated flag and two Int64 bounds raise the new index to 3,077
bytes, exceeding the tested 3,072-byte InnoDB limit with error 1071. Doka
promotes inferred lengths to explicit metadata and cannot rebudget the later
index even though NestedSet's early-finalizer ordering is correct. Explicit
application-domain lengths have separate qualification. This is an internal
provider correctness finding, not proof of a required new public Doka API or
authorization for arbitrary NestedSet identity limits. The corrected private
combined provider passed the same implicit-string schema and public
mutation case on both engines; its relationship-facet correction retains
compatible parent mappings. This satisfies the local implicit-model proof,
while published-package qualification and historical performance cohorts remain
separate boundaries.

Ordinary EF migrations create and upgrade the generated schema independently.
Historical SafeMigrations 10.5.0 MySQL/MariaDB and PostgreSQL adapters deliberately
rejected the automatically scaffolded computed expression as
`opaque_sql_expression`. SQLite's fresh-table route is accepted initially but
fails its own postcondition because generated DDL omits the model's declared
type and NOT NULL facets; the batch rolls back without recording a migration.
Its populated generated ADD route succeeds. This describes the historical
published 10.5.0 graph, not the later local generated-expression implementation.
The current-source contract requires actual fresh and populated generated
migrations, physical type/nullability/storage/expression checks, data and
history preservation on replay, and independent drift rejection. Safe index replay/drift checks on
an ordinary EF-created schema are a separate supported boundary, not evidence
of automatic generated table/column support. Native SQL Server qualification
still requires x64.

The owner-approved 2026-10-25 update now declares published Doka 10.6.2 and
SafeMigrations Core/all four adapters 10.6.0. Their package metadata resolves
the earlier compatibility pin decision: the MySQL adapter requires Doka
`[10.6.2,10.7.0)`. Its fresh 2026-10-25 local public-graph run passed 11,726
cases with zero failures and 2,072 explicit native SQL Server ARM exclusions.
The [provider record](../qualification/provider-migrations.md#published-runtime-qualification-2026-10-25)
keeps these nine projects separate from earlier private passes and native x64 CI.

For an eligible Doka query, the library binds only the exact public
`WithMaterializedSource<TSource, TResult>` method advertised by the actually
configured provider assembly. Weak caches retain neither contexts nor result
sets. Method discovery and delegate publication share an internal mechanism;
each feature retains its own exact signature validator and typed closure
identities. Collectible providers and generic types retain weak ownership,
while ordinary warm lookups reuse their published delegate. Indexed inclusive
queries additionally require the independently verified
public `StraightJoin<TOuter, TInner, TKey, TResult>` definition: two query
sources and three typed expression selectors, with no generic constraints.
Missing required capability preserves the corresponding existing strategy;
malformed required metadata fails explicitly. Each reader requires only its
own capabilities, so a missing ordered-join API cannot disable the strict
indexed path; the ordinary strict route requires that API for final owner
hydration. Current package availability and the resulting reader
matrix belong in [configuration](../configuration.md#current-query-capabilities).

Repeated provider lookups weakly attach an extension-reference snapshot only to
the exact standard `DbContextOptions<TContext>` implementation, whose map is
immutable. Every lookup still checks the context's exact provider name and
reads each extension's current `Info.IsDatabaseProvider` before validating the
actual assembly identities. Custom options and subclasses retain their original
enumeration. The snapshot does not cache an engine, a resolved owner or an
authorization decision, and does not extend its options graph's lifetime.
One array and weak-table bookkeeping are cold per-options costs; concurrent
first callers may create temporary duplicate arrays. No extension-value
immutability or single cold scan is assumed.
Warm hits consult that existing weak entry before runtime generic-type
classification. This avoids recreating the runtime's weak type-metadata cache
after collection while retaining every current provider-metadata check.
Qualification lanes remain distinct from provider publication.

Generated compiled models retain the optional model descriptor, not an
assumption that both public APIs are installed. Their query qualification
independently checks strict source binding and inclusive source binding plus
the two directed library-plan edges against the configured provider. Each
public API invocation still supplies one edge. Released,
source-binding-only, and ordered-provider executions are separate evidence
boundaries; earlier metadata or migration passes cannot establish the final
query gate.

Only eligible `SelectAncestors` and `SelectAncestorsOrSelf` selections
use the indexed Doka strategies, subject to those independent capability gates,
the explicit index and the independent-owner capability. The generated
flag/index model remains portable across supported providers. A schema-only
provider or an ineligible selection retains the established read strategy with
the shared overflow-safe nonleaf predicate; schema availability alone implies
no new-index read-performance improvement.

The indexed strategy shares the completed selected source within one statement
attempt. Independently inspected query-bearing facts first retain a narrow
structural `DISTINCT` query block before the public source binding. The selected
root's complete unique NodeKey, TreeId, and optional Scope identity is part of
that projection; therefore the boundary preserves selected rows rather than
deduplicating report occurrences or physical facts. Plain scalar selections retain their previous
shape. Boundary selection reuses the eligibility result without another
predicate inspection; it does not replace the query-operator pre-pass or the
subsequent independent-source visitor.

This boundary addresses the observed MariaDB merge of a fact `EXISTS` into the
recursive anchor. The merge caused repeated checks across the whole node
population even after eligibility was corrected. MariaDB's documented
derived-table merge restrictions explain the boundary; native results must
still establish work and allocation for the actual source and installed engine.
The change requires no additional provider API, optimizer hint, or public knob.

Source envelopes group physical Scope when configured and TreeId,
retaining maximum Left, minimum Right, and a nonempty-group marker. Candidate
owners use the original unbound mapped root with all application/global
filters intact; source-derived identity points supply the library's Scope/Tree
restrictions. Private translated closed-point ranges on those identities and
nonleaf state retain native mapped types, converters, and collations. This
exposes source-dependent range planning without a public option or index hint.
Strict candidates retain their own physical Key, optional Scope, TreeId, Left,
and Right through DISTINCT. All four original strict comparisons reject
nonmatches on these coordinates before matching keys reach the existing
mapped identity joins. The final mapped aliases retain all four comparisons;
the early filter does not replace their identity or visibility checks. A
necessary envelope cannot substitute for this exact pre-hydration rejection.
Inclusive selected sources additionally capture their generated flag. Every selected
source enters nonleaf mode; only false-flag positive-width sources enter leaf
mode, so a selected thousand-node chain with one leaf has 1,001 source modes.

Inclusive candidate arms select their own physical Key, optional Scope,
TreeId, Left, and Right columns and apply DISTINCT before projecting mode and
mask columns. This aligns EF's native collection identifiers while retaining
equal-bound nodes with different keys. The bounded candidate union receives
one additional dependent materialized-source binding; actual entity payload
and final output pairs do not cross that boundary. The outer binding still
completes the selected source separately.

The final predicates retain exact containment and complete tree identity.
Strict and inclusive paths do
not deduplicate caller occurrences. Inclusive selection uses two bounded
structural lane unions, retaining at most two matching modes per selected
source and one per candidate owner. One public `StraightJoin` call orders
source modes before the completed candidate relation, using mode, complete
partition identity and exact leaf or masked nonleaf coordinates. Equality
projection columns precede range coordinates. All four original comparisons
reject structural nonmatches before payload hydration and remain on each
endpoint's actual aliases after complete-identity joins. These structural sets
preserve collection identifiers without a union over final pair output.

A second invocation orders the candidate-bearing selected relation before
final mapped-owner hydration. The earlier source/candidate edge alone leaves
that later owner alias free to drive repeated broad candidate work while
preserving identical results. Ordering that owner boundary addresses the
specific missing dependency. Source-entity hydration stays ordinary, allowing
selected-source reuse instead of forcing one source lookup per expanded pair.
Other consumer and payload-source joins remain ordinary as well.

Both invocations use the existing verified single-edge public API. No pair
spool, budget increase, new provider API or public option is introduced.
Unique mapped keys, exact filters, original bounds and consumer occurrence bags
remain unchanged. Neither edge supplies a SELECT-wide option, SQL rewriting,
result order, materialization, snapshot or speed guarantee. MySQL semijoin
eligibility can change with local `STRAIGHT_JOIN` edges; exact native work and
consumer filters therefore need their own unchanged-budget qualification.
The late mapped aliases use the existing database query-consistency boundary;
the strategy does not grant a new snapshot or stronger read-uncommitted
guarantee.
Hierarchy-bearing and opaque mixed-direction source predicates retain their
established strategy instead of forcing an eager pivot that can hide selective
input. Non-hierarchy EF fact roots may complete independently after their actual
model and inherited/named filters are inspected. Ordinary captured `Any`,
mapped DbSet properties and `context.Set<T>()` use the same eligibility rules
after EF normalizes their roots at terminal execution. Hierarchy-bearing,
navigation, raw-SQL and opaque roots retain the established strategy.
The [canonical selection rules](../configuration.md#current-query-capabilities)
own the current provider matrix.

Source-sensitive ancestor composition on every options-registered provider
can defer through exact private operations. `NestedSetDeferredQuery` owns five
markers: strict/inclusive ancestors and Path/Any/Nearest. The three filters
defer only when their source already contains an owned marker and retain the
same immediate source/scope/null/endpoint guards. After normalization they
reuse the respective existing cores rather than a separate path/owner contract.
The indexed reader remains independently restricted to Doka, opt-in schema
and verified capabilities.

The stateless `IQueryExpressionInterceptor` installed by the matching native
provider callback's `UseNestedSets()` registration
expands markers after EF's capture/parameter processing and before relational
translation when compilation is needed. The compiling context supplies the
actual model. EF owns captures, cache selection and execution; rebinding a
source before another terminal does not freeze its former expression.
Library-owned late closures are folded without application callbacks.
Model-only registration keeps its non-deferred construction path and emits no
marker.

Compilation-local canonicalization compares only admitted owned visible/unbound
roots after checking their complete restricted grammar. Exact EF metadata,
scalar parameter identities and approved literals stay significant. Custom
objects/arrays and opaque/reducible syntax remain outside comparison, so no
application equality/hash/getter/reduction callback is used. This restores
reference sharing lost during EF extraction for the existing endpoint proof;
it does not intern arbitrary application query trees or retain a global cache.
Cold proof construction moves into compilation. Builder-only savings do not
establish lower full-reader allocation, latency or a warm-cache benefit.

Construction-time field/getter recovery was not chosen: even a getter-free
field read would freeze an earlier `IQueryable` when the caller rebinds it before
execution. Requiring consumers to embed `.Expression` manually was also not
chosen because ordinary LINQ must reach the same qualified access path.
A replacement EF query provider, eager enumeration and private EF service
replacement are unnecessary; the public compilation interception boundary
provides normalized syntax while preserving deferred execution.

Shared runtime-owned syntax/metadata admission is one utility. Source shape,
fact independence, memoization stability and endpoint mapping/visibility remain
separate proofs because they answer different questions. Combining their state
would allow one admitted root or stable path to authorize another endpoint.
This consolidation changes no qualified SQL strategy or result semantics.

Provenance analysis resolves the exact source binder's lexical
parameter and independently recognizes only the verified public ordered-join
definition. That join retains both mapped inputs; it is not another source
binder. Analysis does not trust arbitrary query methods or grant visibility to
another mapping or filter-stripped source.

Both source classifiers inspect child syntax before derived parent types and
require runtime-owned constructor, indexer, and declared-type metadata before
reading their type/assignability information. Application metadata wrappers and
dynamic expressions remain conservative without callbacks or reduction;
ordinary runtime metadata remains supported. The getter-free boundary begins
after expression construction, whose public factories may validate metadata.

The endpoint guard retains exact `(binding call, parent environment)` scopes
within one operation. Stable relative member paths and incoming visibility
form the per-scope memo key. This prevents repeated lexical references from
expanding a shared source DAG while keeping different endpoint members,
named mappings, and environments distinct. Mutable captured queries retain
individual inspection, including runtime queries hidden by object-typed fields.
Opaque properties and static fields cannot establish endpoint mapping or prove
bound visibility through casts, type-as conversions or AsQueryable calls. The
guard never invokes application property getters for this proof; statically
query-typed inputs and ordinary object payloads retain their supported behavior.
There is no inter-operation query or visibility cache.

Immutable coordinate selectors are a separate, metadata-only reuse boundary.
A weak key for the exact finalized `IEntityType` retains the source/candidate
expressions with constant configured property names. Named mappings and different
models never share selectors, and neither selector retains a context, request,
query root, or metadata owner. Upward result projection substitutes the original
caller parameters once, preserving endpoint order and positive distance. Small
fixed-arity substitutions avoid argument arrays without changing simultaneous
replacement or larger-arity behavior. These changes do not cache a completed
query, its visibility proof, or parameter values across operations.

### Consequences

- Good, because applications can add the concrete access path without changing existing hierarchy or query contracts.
- Good, because schema configuration and public provider capability remain independent, explicit boundaries.
- Bad, because another index consumes storage and increases coordinate-write maintenance.
- Bad, because source sharing and index eligibility do not guarantee output-proportional work or one physical temporary table.
- Bad, because a published provider capability still requires qualification of its actual query plans and complete consumer costs.
- Bad, because optional migration adapters require their own generated-column qualification independently of ordinary EF schema migration.
- Bad, because the recorded Doka 10.5.0 implicit-string budgeting failure and private correction cannot qualify the subsequently declared published Doka 10.6.2 binaries without a fresh run.

### Confirmation

The [query qualification](../qualification/query-composition.md#optional-ancestor-access)
records historical native read/write/storage controls, binary provenance,
negative outcomes and mixed costs. The
[provider/migration qualification](../qualification/provider-migrations.md)
separates corrected local owner packages, actual schema/data/history/replay
and the remaining native x64 SQL Server boundary. Implemented
status refers to this NestedSet contract, not provider publication or a hosted
release pass.

The [regression map](../query-composition-regression-coverage.md#optional-ancestor-index-and-source-binding)
owns the exact test inventory. Required controls cover exact public capability
identity and lifetimes; independent endpoint provenance; mapped/filter/bag
semantics; fresh/populated/down/compiled migrations; generated-value rollback;
invalid-coordinate and schema-drift rejection; strict/inclusive native work;
and physical base access separately from temporary candidate CPU.
Endpoint-DAG construction retains 64/128/256 KiB depth-12/24/48 budgets, with
a fresh operation-local resolver under the unchanged 256 KiB alternating-path
limit. A visibility proof must still reject foreign/opaque query carriers
without invoking getters.

The [performance guide](../performance.md#optional-ancestor-index-and-shared-source-boundaries)
defines complete-read, construction, write and index-storage comparison
boundaries. Preserve adverse results, unknown counters and the independent
four-comparison malformed-interval oracle. A `64 * (S + P)` fixture allowance
cannot replace output bags, temporary loops, actual application-table work or
source-cardinality observations. Qualification must identify the exact
source/package hashes and native engine; a command listed in a guide is not
execution evidence.

## Pros and Cons of the Options

### Explicit generated ancestor index with an optional public provider source binding

- Good, because consumers opt into a measurable storage/write trade-off and provider capability is verified without a mandatory provider dependency.
- Bad, because model/migration, fallback, and feature-enabled query paths require independent regression qualification.

### Add the ancestor index and source boundary to every hierarchy

- Good, because one default schema and query strategy would reduce configuration choices.
- Bad, because every writer would pay an unrequested index cost and eager mixed-direction pivots can regress selective queries.

### Retain the current schema and change only the correlated SQL spelling

- Good, because existing deployments would need no schema migration or new provider capability.
- Bad, because the measured LATERAL alternative still scanned broad populations and SQL spelling alone does not bind completed source occurrences.

## More Information

[D-008](D-008-provider-neutral-indexes-and-migrations.md) continues to define
ordinary index ownership and adapter independence. This decision adds an
optional access path; it does not replace existing structural indexes or alter
the composition semantics in D-018. Application metadata, consumer aggregates,
roles, authorization, and temporal edge rules remain consumer responsibilities.

The inclusive owner-hydration strategy reuses the existing generated-index and public-provider
contracts. It introduces no adapter package, compile-time Doka dependency, or
SPATIAL/GiST index contract. Portable generated-schema support remains
unchanged; schema-only readers retain their existing query path and receive an
informational diagnosis when that path is actually requested.

### Re-evaluation Triggers

- EF changes capture extraction, query-parameter nodes, compilation interception or cache behavior. Requalify deferred field/DbSet/Set sources, getter timing, same-query rebinding, scalar cache reuse, foreign-context rejection and downstream endpoint composition before accepting that EF version. Include marked downstream Path/Any/Nearest filters and canonical-root admission, not only the initial ancestor projection.
- Canonical-root comparison admits a custom value, array, metadata wrapper or reducible node, or merges distinct mapping/parameter/literal roots. Requalify callback counters, independent bindings and full result bags; keep canonicalization compilation-local rather than extending a global cache.
- A shared syntax change admits application-owned expression/metadata callbacks, or a proof begins borrowing another analysis's mapping, filters or stability. Retain the adversarial wrappers and independent proof-state tests rather than weakening admission.
- A Doka release changes either public capability signature, the completed-source mapping/filter/paging/statement-attempt guarantees, or its per-call directed-edge guarantee. Requalify exact API binding, provenance, compiled/quoted query paths, all semantic cases, and native comparisons before accepting that release.
- A final mapped-owner alias is scheduled before its matched structural candidates, or source hydration becomes repeated per output pair. Inspect both directed edges and leave unrelated joins free; requalify exact bags, collection identifiers, native loops and unchanged work budgets rather than inferring performance from SQL shape.
- Native plans for a supported workload exceed the established input/work or allocation budget, or a baseline control improves while the provider path regresses. Diagnose the emitted expression and physical plan before adding another provider API or index.
- Sparse deep or wide inclusive MariaDB profiles regress to population-scale work or fail their native budget, including when application global filters fix Scope. Reopen qualification and remeasure the exact source before accepting a change. Preserve those application filters and retain temporary comparisons separately from physical base-table access while diagnosing the plan.
- Coordinate-write maintenance or physical index growth exceeds an application's accepted budget. Compare the complete read/write workload before changing the default or index key order.
- A provider changes generated-column indexing or migration support, or EF changes final physical mapping/runtime annotations. Requalify fresh/populated migrations, generated compiled models, ownership, and tracked refresh.
- Doka corrects automatic string-length metadata ownership or index-width rebudgeting. Require the independently reproduced implicit Scope/TreeId/NodeKey case to create the complete ancestor index on both engines before removing its qualification boundary; do not infer the fix from explicit-length models.
- Optional SafeMigrations adapters reject or misclassify the actual scaffolded computed-column/index contract. Persist a precise adapter finding; retain the independently supported ordinary migration path.
- EF changes standard options-map membership or its virtual extension enumeration, or runtime weak/type-metadata ownership changes. Requalify custom/subclass options, mutable provider classification, post-GC allocation and collectible lifetimes before changing snapshot admission.

### Decision History

- 2026-10-19: Accepted an explicit additive ancestor index and exact public Doka source binding, with an independently required ordered edge for inclusive reads. Schema availability does not imply read eligibility or a workload benefit.
- 2026-10-19: Retained narrow structural envelopes/lanes, original containment and application filters after native materialization and range-planning regressions. Deferred payload hydration preserves mapped aliases and source occurrences.
- 2026-10-20: Admitted inspected independent fact roots while retaining hierarchy, navigation, named/inherited-filter and opaque-source safeguards. Shared syntax discovery does not merge distinct provenance, visibility and stability proofs.
- 2026-10-20: Qualified strict Unicode scalar-length handling separately from the provider's composite-index byte budget and generated-value readback contracts.
- 2026-10-23: Verified current local generated-expression/CHECK/SQLite migration contracts through actual schema/data/history/replay and real drift rejections; published-package and native SQL Server qualification remain explicit boundaries.
- 2026-10-24: Directed final owner hydration after matched selected candidates through the existing ordered-join API, in addition to source-mode/candidate ordering. Kept source hydration ordinary and preserved result/work contracts; native qualification remains independent of this structural decision.
- 2026-10-24: Deferred source-sensitive ancestor selection to EF's public compilation boundary on all options-registered providers, with marked downstream filters using the same normalized cores. Kept owned-root sharing compilation-local and rejected construction-time capture freezing/manual-root-only UX; shared syntax admission while retaining distinct provenance proofs.
- 2026-10-25: Reused verified public source binding for eligible strict ancestors without generated ancestor schema: completed structural sources supply ordinary Right-index ranges, followed by exact identity, visibility, and containment checks. Only positively identified MySQL additionally completes matched key occurrences to separate repeated-direction optimizer search; MariaDB and unknown engine identification keep source-only binding after the all-engine pair boundary exhausted the recorded MariaDB branched-chain sample container. Both routes retain ordinary source hydration, directed final owner hydration and the walk bag through existing public provider APIs. Hierarchy-correlated and capability-absent selections retain their established route; no public API or schema option was added. Historical ten-million-node observations, failed controls, and targeted checks remain separate from final complete-read and allocation qualification.
- 2026-10-25: Updated the qualification pins to published Doka 10.6.2 and SafeMigrations Core/adapters 10.6.0 after owner approval and inspection of their published package metadata. Closed the pending compatible-version selection; retained dated local results and left fresh public-binary runtime/migration qualification separate.
- 2026-10-25: Verified the fresh local published graph across nine executable projects: 11,726 passed, zero failed and 2,072 native SQL Server ARM exclusions. Completed 68 plain measurement records and parsed 68 separate diagnostic captures with zero lost events, retaining mixed costs and sampling/host limits. Historical/private results and native-x64 requirements remain separate; this is not hosted release qualification.
- 2026-10-25: Reused only exact standard options-map membership under weak keys during repeated public provider-feature lookup. Retained fresh extension metadata, actual assembly validation and custom-options enumeration; rejected caching resolved owners based only on map immutability. Runtime and allocation qualification remain separate from this ownership decision.

- 2026-10-26: D-020 amends optional reflected Doka binding and the no-shipping-provider-dependency assumption; the generated-index opt-in and historical evidence remain intact.

### Implementation References

- [Ordinary strict ancestor source bounds](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.AncestorPairs.cs)
- [Unindexed source-boundary and selection controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/IndependentAncestorEnvelopeTests.cs)
- [Mixed-direction native capacity profiles](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Performance/CompositionCapacityProfiles.Mixed.cs)
- [Exact deferred query dispatch](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetDeferredQuery.cs)
- [Strict/inclusive markers](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetDeferredQuery.Ancestors.cs)
- [Source-preserving filter markers](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetDeferredQuery.Filters.cs)
- [Compilation-local admitted root sharing](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetNormalizedQueryRoots.cs)
- [Stateless compilation interceptor](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQueryExpressionInterceptor.cs)
- [Library-owned late closure normalization](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetNormalizedQueryClosure.cs)
- [Shared syntax ownership admission](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuerySyntax.cs)
- [Ordinary capture, getter timing, rebinding and cache controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionAncestorIndexTests.CapturedFacts.cs)
- [Normalized downstream-filter controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionAncestorIndexTests.NormalizedFilters.cs)
- [Public optional index builder](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetBuilder.AncestorIndex.cs)
- [Early declaration convention](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetAncestorIndexConvention.cs)
- [Generated flag convention](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetAncestorIndex.cs)
- [Structural index reconciliation](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetIndexes.cs)
- [Indexed ancestor expressions](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.AncestorIndexPairs.cs)
- [Bounded inclusive structural lanes](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.AncestorIndexPairs.Lanes.cs)
- [Private mapped native point-range marker](../../src/Doka.EntityFrameworkCore.NestedSet/Providers/Queries/NestedSetNativePointRange.cs)
- [Direct public ordered-join binding after D-020](../../src/Doka.EntityFrameworkCore.NestedSet.MySql/Queries/MySqlNestedSetProvider.Queries.cs)
- [Consumed common query-provider boundary after D-020](../../src/Doka.EntityFrameworkCore.NestedSet/Providers/NestedSetProviderCapabilities.Queries.cs)
- [Requested-feature EF diagnostics](../../src/Doka.EntityFrameworkCore.NestedSet/Diagnostics/NestedSetQueryDiagnostics.cs)
- [Index configuration tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/AncestorIndexTests.cs)
- [Provider capability tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaSourceBindingTests.cs)
- [Requested-feature diagnostic controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaQueryFeaturesTests.Diagnostics.cs)
- [Ordered-join capability and rejection tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaStraightJoinTests.cs)
- [Ordered-join endpoint provenance tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaStraightJoinTests.Endpoints.cs)
- [Ordered-join collectible lifetime tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaStraightJoinTests.Lifetime.cs)
- [Endpoint provenance tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionSourceBindingEndpointTests.cs)
- [Endpoint DAG allocation and adversarial capture tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionSourceBindingEndpointTests.Scaling.cs)
- [Operation-local endpoint resolver](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQueryEndpoint.Resolver.cs)
- [Stable metadata eligibility](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQueryEndpoint.Stability.cs)
- [Visibility allocation controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionVisibilityTraversalTests.cs)
- [Existing isolated measurement collection](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Infrastructure/TestCollections.cs)
- [Ordinary migration tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Integration/AncestorIndexMigrationTests.cs)
- [Compiled model tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Integration/AncestorIndexCompiledModelTests.cs)
- [Generated-value lifecycle tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Integration/AncestorIndexLifecycleTests.cs)
- [Generated signed-boundary insert and upgrade tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Integration/AncestorIndexBoundaryTests.cs)
- [Optional adapter tests](../../tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests/Integration/Indexes/AncestorIndexSafeMigrationTests.cs)
- [Semantic and composition tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/AncestorIndexQueryTests.cs)
- [Original containment oracle for malformed split intervals](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/AncestorIndexQueryTests.MalformedBounds.cs)
- [Shared signed-domain malformed interval oracle](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/MalformedIntervalTestSupport.cs)
- [Released-provider malformed downward query controls](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Querying/Composition/MalformedSubtreeTestBase.cs)
- [Generated property-bag identity and exact recovery tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition/Models/AncestorIndexQueryTests.GeneratedBags.cs)
- [Persistent native ancestor work controls](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Querying/Composition/Performance/AncestorIndexWorkTestBase.cs)
- [Post-structural-write generated refresh recovery tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Integration/AncestorIndexLifecycleTests.LateRollback.cs)
- [Focused regression map and allocation observations](../query-composition-regression-coverage.md#optional-ancestor-index-and-source-binding)
- [Historical local and native qualification (2026-10-19)](../qualification/query-composition.md#optional-ancestor-access)
- [Performance boundaries](../performance.md#optional-ancestor-index-and-shared-source-boundaries)


### Sources

- [EF 10.0.13 standard options map](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/DbContextOptions.cs) (primary source; inspected 2026-10-25)
- [EF 10.0.13 generic options replacement](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/DbContextOptions%60.cs) (primary source; inspected 2026-10-25)
- [ConditionalWeakTable key and value lifetimes](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0) (primary source; retrieved 2026-10-25)
- [.NET 10.0.13 weak runtime type-metadata cache](https://github.com/dotnet/runtime/blob/v10.0.13/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.CoreCLR.cs) (primary source; inspected 2026-10-26)
- [Published Doka 10.6.2 metadata](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.mysql/10.6.2/doka.entityframeworkcore.mysql.nuspec) (primary source; retrieved 2026-10-25)
- [Published SafeMigrations 10.6.0 metadata](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.safemigrations/10.6.0/doka.entityframeworkcore.safemigrations.nuspec) (primary source; retrieved 2026-10-25)
- [Published SafeMigrations MySQL 10.6.0 dependency range](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.safemigrations.mysql/10.6.0/doka.entityframeworkcore.safemigrations.mysql.nuspec) (primary source; retrieved 2026-10-25)
- [EF public query-compilation interception](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.iqueryexpressioninterceptor.querycompilationstarting?view=efcore-10.0) (primary source; retrieved 2026-10-24)
- [EF 10.0.13 parameter extraction and compiled-query cache boundary](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/QueryCompiler.cs) (primary source; archived source inspected 2026-10-24)
- [MySQL 8.4 ordered inner join](https://dev.mysql.com/doc/refman/8.4/en/join.html) (primary source; retrieved 2026-10-19)
- [MySQL 8.4 semijoin restrictions](https://dev.mysql.com/doc/refman/8.4/en/semijoins-antijoins.html) (primary source; retrieved 2026-10-19)
- [MariaDB targeted versus SELECT-wide join ordering](https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/query-optimizations/index-hints-how-to-force-query-plans) (primary source; retrieved 2026-10-19)
- [MariaDB derived-table merge prerequisites](https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/query-optimizations/optimizations-for-derived-tables/derived-table-merge-optimization) (primary source; retrieved 2026-10-20)
- [MariaDB view MERGE restrictions, including DISTINCT](https://mariadb.com/docs/server/server-usage/views/view-algorithms) (primary source; retrieved 2026-10-20)
- [EF Core computed columns](https://learn.microsoft.com/en-us/ef/core/modeling/generated-properties#computed-columns) (primary source; retrieved 2026-10-19)
- [EF Core compiled model regeneration and limits](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-models) (primary source; retrieved 2026-10-19)
- [PostgreSQL 17 generated columns](https://www.postgresql.org/docs/17/ddl-generated-columns.html) (primary source; retrieved 2026-10-19)
- [SQLite generated columns](https://www.sqlite.org/gencol.html) (primary source; retrieved 2026-10-19)
- [MySQL 8.4 generated-column indexes](https://dev.mysql.com/doc/refman/8.4/en/generated-column-index-optimizations.html) (primary source; retrieved 2026-10-19)
- [MariaDB virtual-column optimizer support](https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/query-optimizations/virtual-column-support-in-the-optimizer) (primary source; retrieved 2026-10-19)
- [SQL Server indexes on computed columns](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/indexes-on-computed-columns) (primary source; retrieved 2026-10-19)
