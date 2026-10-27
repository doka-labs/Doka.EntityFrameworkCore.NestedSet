---
id: D-018
status: accepted
date: 2026-10-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Generic upward and downward query composition, visibility, and consumer-owned reporting policies"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-018 -- Compose generic hierarchy queries through ordinary EF expressions

## Context and Problem Statement

Single-anchor tree queries do not express multi-anchor reporting, per-source
ancestor selection, or walks that change direction while retaining their
origins. Applications also need different answers to two distinct questions:
whether any visible ancestor matches, and whether the structurally nearest
candidate matches. Hidden intermediate nodes and temporal branch rules must
not silently turn one question into the other.

Reporting and group-based supervisor lookup supplied concrete consumers.
Their measurement dates, metadata, memberships, roles, ambiguity rules, and
chain limits are application policy. Embedding those policies in NestedSet
would couple a structural library to unrelated domains. A second query DSL or
provider would also duplicate EF's expression and execution responsibilities.

The current SignalLab consumer is an independently designed fictional
acquisition lab. Devices may nest freely, channel definitions are synthetic,
and pulse, sweep and burst events have distinct physical formats and independent
identities. Explicit projections normalize those inputs without collapsing
equal-valued events. Its demonstration data, tags and connection windows are
separate from generic hierarchy contracts. Tests and benchmarks exercise those
same components rather than maintaining another reporting implementation.

## Decision Drivers

- Preserve ordinary DbSets, finalized mappings, optional Scope, and TreeId.
- Compose both directions without preliminary entity loads or client traversal.
- Preserve row multiplicity, intermediate endpoints, and structural distances.
- Distinguish visible existence from a complete visible path and nearest owner.
- Keep parameterization, filtering, tracking, and async execution under EF.
- Bound construction allocations and DTO materialization independently of tree size.
- Qualify SQL shapes and native plans before expanding consumer samples.
- Keep consumer rules and finite multi-statement orchestration in the samples.

## Considered Options

- Six deferred compositors over existing EF queries
- Consumer-specific methods in the library
- A dedicated hierarchy query provider and fluent query language

## Decision Outcome

Chosen option: "Six deferred compositors over existing EF queries", because
the concrete consumers share structural joins and filters while retaining
different business rules. The existing facade can return normal IQueryable
expressions without another execution engine, registration option, service,
or dependency.

The three existing facades expose the same six deferred operations:

| Operation | Structural contract |
| --- | --- |
| `SelectSubtrees` | Project selected anchors and their downward occurrences with mapped distance. |
| `SelectAncestors` | Project each selected source and its strict ancestors with mapped distance. |
| `SelectAncestorsOrSelf` | Include self in the upward projection. |
| `WherePathEdges` | Require every actual direct edge on the complete visible endpoint path. |
| `WhereAnyAncestorOrSelf` | Require a matching visible owner, without implying complete-path validity. |
| `WhereNearestAncestorOrSelf` | Select the nearest candidate, then test its value and complete visible path. |

Construction is synchronous and performs no I/O. The application's async
terminal operation executes the completed ordinary EF query.

Pairs preserve the incoming relational bag and derive distance from mapped
Depth. Query filters apply to every hierarchy alias. Raw pair projections can
span hidden intermediates, and Any can match a visible owner above one.
Explicit path and nearest checks require the complete visible path. Nearest
selects the candidate first, including self, and only then tests its value and
path; rejection never falls back to a farther matching candidate.

Mapped entity equality resolves endpoint identity through EF. Scope and TreeId
remain explicit constraints even if they are not primary-key properties. A
small selected-alias check rejects constructed endpoints and wrong named
mappings without forbidding unrelated fact roots used as payload. Expression
substitution follows parameter identity, never parameter names or compiled
application delegates.

Public path composition and structural storage share the typed nullable
Parent join and its principal-collation rule. Valid stored bounds and depths
remain a precondition; these queries do not replace invalid-structure
validation or repair. A self path has no incoming edge to validate. Hidden
intermediates cannot be skipped by complete-path or nearest-owner checks.

Providers may use equivalent physical expressions for joins, candidate lookup
and path proof. Those choices must preserve endpoint identity, visibility,
candidate-before-value ordering and public row multiplicity. SQL short-circuit
evaluation, a particular index or optimizer plan, and constant execution cost
are not public guarantees. The [performance guide](../performance.md#query-composition-and-actual-consumer-profiles)
owns strategy trade-offs, construction budgets and capacity qualification;
the [dated record](../qualification/query-composition.md#semantic-and-native-work-evidence) preserves
historical measurements and their limitations.

Invariant Doka long-path proofs partition invalid-child coordinates into
adaptive blocks. Full interior blocks use maximum Right summaries; both
endpoint fringes retain exact Left bounds. Mapped scope/tree/block joins and
independent grouped relations prevent the repeated global failure scan observed
with the earlier SUM proof. Three population passes are an explicit tradeoff,
not a constant-cost promise. Row-dependent rules retain their per-row direct-edge
proof. No stored helper column, schema index, public option or consumer-specific
policy is introduced.
Independent nonrelational nearest candidates use exact Left and Right fringes
plus independent maximum-Left summaries of complete Left/Right block pairs.
On valid laminar bounds, the nearest coordinate is the first present maximum
from the Left fringe, Right fringe, then interior. The actual owner is resolved
by mapped scope/tree/Left identity before its value and complete owner-to-lower
path are tested. Nullable coordinate selection admits no sentinel or rejection
fallback. No caller source is copied or grouped; paging, volatile ordering and
original occurrences remain on that source. Noncrossing intervals give a linear
number of distinct block pairs; actual optimizer work remains natively qualified.
Row-dependent or query-bearing candidates
retain their original correlation. The same physical tradeoffs and native
work qualification apply; a selected-key fence alone did not bound MariaDB's
repeated candidate scans.

SignalLab catalog validation and aggregate reads share one explicit read snapshot.
Supervisor reduction and finite chain orchestration retain their own statuses,
origins, cycle detection, and pre-buffer limits. These are sample internals,
not additional library APIs. A small shared sample helper owns a read
transaction only when no adequate application transaction already exists.
It never changes server isolation configuration or commits a caller transaction.

Sample person ranking separates canonical ranks from original evidence.
Nearest distance and its best priority determine one rank per person; all
people at the selected rank participate in ambiguity. Selection occurs once
per origin, then a unique person rejoins every supporting occurrence. Request
collections use a local parameterization marker rather than a new query cache
or a global EF setting. Ordinary EF execution still performs parameter extraction
before cache lookup, so construction, prepared execution and complete consumer
allocations require separate measurements.

Query scalar reads use an exact mapped CLR member where metadata proves its
identity; shadow, complex and indexer roles retain EF property markers. This
reduces warm parameter-extraction work without calling application getters.
Short paths compare their existing visible endpoint aliases directly; a
two-edge path retains one visible middle alias, both containments, consecutive
depths and both edge rules. Exact nullable Parent types and principal collation
remain unchanged. No query cache or internal EF service replacement is added.

The sample shares origin construction across status and candidate composition.
A unique person in a single selected group already carries complete witness
evidence, so only the two single-group windows omit the repeated candidate
join. Other windows retain every occurrence. Unique, priority and Nth-person
selection keep their caller-owned winning evidence snapshot; `All` keeps its
existing direct forwarding. Only a fresh reader-owned buffer may be
retained directly. Chain hops do not allocate an additional one-element array
per continuation or reserve routing storage from an unrelated frontier size.

The SignalLab sample qualifies distinct measured Node/Day paths before restoring the
original fact bag. A scalar Anchor/Node/Day boundary shares expensive period and
metadata decisions across metrics and sources. The restored facts still pass
their own measurement-type predicate. This avoids repeated database proof
work while retaining equal-valued facts, overlapping anchors, dates, nearest
rejection and the original report snapshot. Construction and complete-operation
allocation trade-offs require their own measurements.

The SignalLab builder shares immutable request-free projection, scalar and correlation
expressions. Its privately owned measurement and period roots are bound directly
per construction; external query sources retain the generic provenance guard.
No context, request, finished query or scalar parameter value is globally cached.
Metadata filters precede the larger temporal expression: both only reject the
same Anchor/Node/Day row, and owner selection still uses the unchanged tree. This
avoids revisiting temporal expressions in later metadata provenance checks.
Both distinct proof boundaries and the original fact bag remain.

The earlier construction-only experiment rejected direct roots because its
construction CPU increased. Complete-read profiling prompted a separate
evaluation of direct roots together with the local filter reorder. The complete
read qualification records the resulting costs and native work; the earlier
construction record remains historical evidence with its original boundaries.
There is no reusable query compiler or context/model query cache.

The correlated existence builder retains its source's static element type and
uses an explicitly typed predicate lambda. Generic static metadata identifies
only the closed public `Queryable.Any` overload. Nine fixed report projections
use private intermediate DTOs; their initialization retains no request, query
root, model or context. The qualified DISTINCT grain remains scalar and exact
before all physical measurements are restored. Anonymous join keys remain.

Only the SignalLab sample's SQLite queries explicitly use one collection parameter
per anchor, channel and acquisition-port selection. This preserves warmed SQL-template reuse
without changing provider-wide options. PostgreSQL already reuses native-array
templates. Doka retains its default cardinality policy: forcing JSON collections
eliminated template generation but regressed a predecessor MySQL wide workload into a
timeout and increased deep summed iterator work. The rejected variant and the
selected positive/rejection cases are retained in the dated qualification.
Pinned MySQL consumer fixtures now check summed executed iterator outputs as
well as existing maximum-work bounds. These empirical budgets do not claim
physical I/O, distinct rows or global complexity bounds.

Four fixed catalog projections have their own initialization boundary, so
construction-only callers do not initialize catalog expression graphs. Scalar
holders belong to one construction and bind distinct instance fields for the
requested classification, field and value. Criterion text fields are readonly.
Owner-only holders also prepare the two comparison suffix strings. This removes their request-side expression
interpretation while retaining ordinary indexed equality and stored-column
suffix equality. Catalog holders do not allocate unused owner suffix strings.
Empty text and literal suffix characters remain ordinary values.

Visibility inspection reuses only the exact hierarchy binding supplied to the
current operation, after a complete positive scan. It uses no visited-node
collection. Opaque expression dispatch or extensible reflection metadata in
that candidate disables reuse; application query getters are never executed.
External payload syntax retains its original inspection. This is not a linear
complexity guarantee for arbitrary shared payload graphs.

Two alternatives were evaluated and rejected. Direct public `EF.Parameter`
calls over scalar constants can merge equal-valued operands and change the
warmed query shape when values become distinct. Request-local member slots
preserve independent positions instead. General reference-identity visited
sets, including an inline-storage variant, increased actual construction
allocation and did not establish a CPU advantage. Their broader scope does not
justify keeping them alongside exact-bound reuse. The dated qualification
retains the measurements and adverse results.

The scalar catalog transports only requested type/field and exact
type/field/value combinations. Database collation can overmatch these SQL
predicates, so ordinal post-read validation remains mandatory. Catalog
authorization, common units, owner modes and contradictions still fail closed.
Snapshot grouping uses ordered local indexes; result-grid sizing is checked
before allocating its exact output array. Duplicate SQL groups remain errors,
and missing cells remain distinct from measured zero.

The sample's `All` mode concatenates origin markers and direct evidence rather
than joining the complete evidence relation back to every origin. This adds
one real marker for each nonempty origin but removes the observed repeated
evidence scan. Existing physical row and overflow limits count those markers;
they are neither raised nor bypassed. Other person modes retain their
server-proven uniqueness and admitted-evidence boundary.

Sample group ordinals use ordinary ordered `SelectMany` with `Skip`/`Take`
so EF can translate the ordinal to a partitioned window. Structural type
selection retains the mapped path before membership, routing and validation.
Eligible selection deduplicates qualified groups, carries the chosen distance,
and rehydrates the exact visible endpoint keys. Person qualification runs again;
the person-independent endpoint/date path proof need not be rebuilt. A zero
offset keeps the grouped relation independent before origin correlation.
Missing ordinals contribute no selected key, preserving explicit NotFound
outcomes without reserving a technical key value.

Doka strict ancestor pairs independently retain only nonleaf structural
coordinates and rejoin mapped owners after containment. This adds a population
pass, but avoids a repeated whole-scope owner scan per origin without carrying
application payload. It never copies the selected source or derives structural
candidates from potentially invalid ParentId links. Unranked sample queries
retain their inclusive relation only when an explicit start group and the
membership primary key prove at most one origin. Unknown cardinality uses
strict ancestors plus exact authorized self memberships. The narrow population
pass also applies when such a request happens to have one origin; there is no
additional count query or cardinality guess.

Three portable alternatives and an actual MySQL LATERAL range failed new
selective-work probes; two alternatives also regressed existing broad-origin
budgets. They were rejected, not activated behind the optional engine API.
The maintainer explicitly requested a separate source-sharing/index contract
after this evidence. Its public `HasAncestorIndex` API and optional provider
source binding are now implemented in the working source under
[D-019](D-019-optional-ancestor-index-and-source-binding.md). Its 2026-10-19 local
qualification used an unpublished Doka feature build; mixed performance results
and the exact recorded source are retained in the
[dated qualification](../qualification/query-composition.md#optional-ancestor-access).
Those additions do not turn the rejected historical alternatives into qualified
ancestor-cost fixes. Activation retains the supported public
provider boundary, without changing bounds to Parent traversal or duplicating
volatile/paged source evaluation.

### Consequences

- Good, because both consumers reuse one generic structural contract while
  ordinary EF joins and aggregates remain visible and composable.
- Good, because scalar reports need no tracked tree and no per-node I/O.
- Good, because hidden-path, nearest-selection, identity, and bag semantics
  have independent positive and rejection tests.
- Bad, because arbitrary application expressions still depend on provider
  translation and may fail before SQL at the terminal operation.
- Bad, because complete-path checks cost database work proportional to the
  relevant shape; broad overlapping requests can have large valid results.
- Bad, because multi-hop consumer orchestration needs explicit snapshot,
  ambiguity, cycle, and budget policy rather than a universal convenience method.

### Confirmation

Run the Unit project with `--filter FullyQualifiedName~Composition` and each
provider project with `--filter FullyQualifiedName~HierarchyComposition` in
Release. Expect valid pairs and bags, no cross-tree/scope substitution, named
mapping rejection before SQL, distinct Any/Nearest visibility, no fallback,
one async statement per scalar or aggregate terminal, and no hierarchy entities
in scalar results. Additional collection includes and EF's split-query policy
remain outside that one-statement contract.
The same suite names apply to MySQL/MariaDB, PostgreSQL, SQLite, and SQL Server.
Native SQL Server runtime confirmation requires x64; ARM skips are not passes.

Run the actual sample builders through their pure request tests and provider
report/supervisor suites. Their commands, budgets, snapshots, and migrations
are qualified separately from the library's structural contracts. Run optional
BenchmarkDotNet construction and relational profiles for timing distributions;
timing is not a CI or release gate.

The contract-to-test mapping and exact execution evidence belong to the
regression and performance guides. Commands written here are reproducible
procedures, not claims that the complete final qualification has passed.

## Pros and Cons of the Options

### Six deferred compositors over existing EF queries

- Good, because it shares the concrete structural primitives without replacing
  EF's provider, execution strategy, or materialization pipeline.
- Good, because explicit projections retain origins and application payload.
- Bad, because the endpoint guard must qualify supported projection forms and
  the application must still choose its own grain and ordering.

### Consumer-specific methods in the library

- Good, because one narrow domain could obtain a shorter report or supervisor call.
- Bad, because roles, dates, metadata, and ambiguity are not NestedSet invariants;
  unrelated consumers would inherit or bypass another domain's policies.

### A dedicated hierarchy query provider and fluent query language

- Good, because a separate language could constrain every supported operation
  and centralize diagnostics for that language.
- Bad, because it duplicates EF query infrastructure, fragments provider
  compatibility, and requires another extensibility and execution contract
  without a consumer that needs those costs.

## More Information

The query guide owns public semantics. Sample guides own their business rules.
The initial shipped API remains the compatibility baseline; composition adds
methods in the 10.1.0 feature contract without changing existing mutation or
schema contracts. Engine-specific SQL syntax requires positive configured-family
evidence. The prepared Doka contract is public
`DatabaseFacade.IsMariaDb()`, read from provider options independently of supplied
or compiled models and without database I/O. Current public availability belongs
in the [canonical capability matrix](../configuration.md#current-query-capabilities).
NestedSet binds this optional public method once per actual provider assembly;
it adds no provider package dependency and never reads private configuration.
Missing API preserves the portable Doka shape and its documented MySQL work
limitation; only an explicit MySQL result enables the separate physical form.
A malformed existing API fails clearly instead of guessing a server family.

### Re-evaluation Triggers

- A synthetic consumer changes topology, acquisition formats, event keys or
  result grain: retain each positive/rejection contract and requalify native
  work, construction and materialization on the new source. Historical numbers
  must not become new-model qualification by changing a label or source link.
- **Local reuse and measurement:** an EF patch changes scalar extraction or
  equal/distinct parameter sharing, runtime expression dispatch changes, or
  an actual consumer's shared payload graph makes repeated visits dominate
  construction. Compare the complete read and existing construction budgets
  before broadening reuse; retain opaque-dispatch rejection controls. A BDN
  adaptive-stage change or renewed chronological drift requires a matched
  protocol review, not removal of slow samples.
- **Collection command templates:** an EF/provider patch changes `canCache`,
  collection translation or native cardinality. Observe actual SQL-template
  generation separately from LINQ query compilation, and compare native broad
  and deep work before changing a provider's per-query collection policy.
  A template-cache improvement alone cannot justify more server work.
- **Mapping and EF translation:** a required projection cannot retain its
  mapped endpoint, or an EF patch changes entity equality, parameter
  substitution, query filters, aggregate preparation, conditional expressions
  or row identifiers, or Doka changes its public configured-family contract.
  Add the exact positive and rejection cases, then rerun
  mapping, navigation, scalar and provider translation coverage before changing
  the alias resolver or physical form.
- **Provider plans and cost:** a native plan repeats candidate/path scans,
  expands a large intermediate relation before selective payload filtering,
  or loses an intended independent relation. Retain the failing profile and
  compare selective/broad, wide/balanced and deep acceptance/rejection cases
  using actual row counts, loops, parameters and allocations. Correct the
  physical expression without weakening semantics, budgets or populations.
- **Consumer and compatibility boundaries:** an actual builder exceeds its
  allocation/transport budget, another maintained consumer needs a shared
  orchestration policy, or trimming, NativeAOT or compiled queries are
  requested. Measure the complete existing consumer and make a separate
  compatibility or boundary decision with independent semantic tests and
  actual package/runtime evidence.

### Decision History

- 2026-10-16: Accepted six deferred compositors over ordinary EF queries, with source bags, exact hierarchy identity, complete visible paths and nearest-owner selection kept separate from consumer business rules.
- 2026-10-17: Replaced repeated global failure scans with partitioned path and nearest-candidate relations after native work regressions; retained dense, hidden-edge, empty-fringe and mixed-direction counterexamples.
- 2026-10-18: Restricted reuse to owned immutable syntax and request-local values; qualified collection transport by provider rather than imposing one SQL shape.
- 2026-10-19: Recorded the optional ancestor index/source-binding contract separately in D-019; ordinary queries and migrations remain independent.

- 2026-10-26: The typed public Doka integration reference follows D-020; generic composition semantics and historical qualification remain unchanged.
- 2026-10-26: The owner selected an independently synthetic SignalLab model for sample, test, benchmark and migration consumers. Source-specific acquisition formats, independent event identities and newly designed fixtures retain the existing hierarchy, multiplicity, owner, reader and allocation contracts. Historical measurements keep their original source and workload boundaries; they are not reassigned to the redesigned model.

### Implementation References

- Historical predecessor construction qualification (planned documentation: `../qualification/query-performance.md#historical-report-construction-and-materialization`)
- [Public query contracts](../query-composition.md)
- [Shared composition implementation](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.Composition.cs)
- [Selected subtree pairs](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.SubtreePairs.cs)
- [Complete path proof](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.Path.cs)
- [Any and nearest decisions](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.AncestorDecisions.cs)
- [Shared structural expressions](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.StructuralExpressions.cs)
- [Partitioned long-path proof](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.PathFailures.cs)
- [Partitioned nearest candidates](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.NearestCandidates.cs)
- [Shared coordinate arithmetic](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQuery.CoordinatePartitions.cs)
- [Selected endpoint validation](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Queries/NestedSetQueryEndpoint.cs)
- [Mapped Parent join](../../src/Doka.EntityFrameworkCore.NestedSet/Mapping/NestedSetParentExpressions.cs)
- [Fast Doka SQL boundary regression](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/CompositionPathTranslationTests.cs)
- [Typed public Doka integration after D-020](../../src/Doka.EntityFrameworkCore.NestedSet.MySql/Queries/MySqlNestedSetProvider.Queries.cs)
- [Actual typed provider strategy and identity tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaQueryFeaturesTests.cs)
- [Actual SignalLab query pipeline](../../samples/SignalLab/Reporting/SignalReportBuilder.Query.cs)
- [SignalLab execution and snapshot ownership](../../samples/SignalLab/Reporting/SignalReportBuilder.cs)
- [Shared sample snapshot](../../samples/Shared/SampleReadSnapshot.cs)
- [Composition regression mapping](../query-composition-regression-coverage.md)
- [Performance contract](../performance.md)
- [Matched cost qualification](../qualification/query-composition.md#semantic-and-native-work-evidence)

The cited EF aggregate preparation, navigation expansion, row-identity and
scalar-default implementations are unchanged between 10.0.12 and 10.0.13.
That supports these pipeline explanations at the package minimum. Runtime
qualification still identifies the patch actually executed; matching source
files do not replace a full provider run on another patch.


### Sources

- [EF 10.0.13 relational command cache](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/Internal/RelationalCommandCache.cs) (primary source; retrieved 2026-10-18)
- [.NET 10.0.12 typed and inferred Lambda construction](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Linq.Expressions/src/System/Linq/Expressions/LambdaExpression.cs) (primary source; retrieved 2026-10-18)
- [EF 10.0.13 public parameter marker](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/EF.cs#L74) (primary source; retrieved 2026-10-18)
- [EF 10.0.13 expression equality](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/ExpressionEqualityComparer.cs#L367) (primary source; retrieved 2026-10-18)
- [.NET 10.0.12 visitor dispatch](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Linq.Expressions/src/System/Linq/Expressions/ExpressionVisitor.cs#L35) (primary source; retrieved 2026-10-18)
- [.NET 10.0.12 custom expression dispatch](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Linq.Expressions/src/System/Linq/Expressions/Expression.cs#L123) (primary source; retrieved 2026-10-18)
- [.NET immutable expression-tree reuse](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/expression-trees/expression-trees-translating) (primary source; retrieved 2026-10-17)
- [.NET typed expression equality](https://learn.microsoft.com/en-us/dotnet/api/system.linq.expressions.expression.equal?view=net-10.0) (primary source; retrieved 2026-10-17)
- [EF 10.0.13 join extraction and partitioned ordinal translation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs#L3168-L3239) (primary source; retrieved 2026-10-17)
- [EF 10.0.13 correlated SelectMany translation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.cs#L1148-L1164) (primary source; retrieved 2026-10-17)
- [EF 10.0.13 parameter extraction before query-cache lookup](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/QueryCompiler.cs) (primary source; retrieved 2026-10-17)
- [EF 10.0.13 warm expression parameter processing](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/ExpressionTreeFuncletizer.cs) (primary source; retrieved 2026-10-17)
- [EF 10 parameterized collection defaults and per-query overrides](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes#parameterized-collections-now-use-multiple-parameters-by-default) (primary source; retrieved 2026-10-17)
- [EF 10 provider options](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.idbcontextoptions?view=efcore-10.0) (primary source; retrieved 2026-10-17)
- [EF public service access for extensions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.accessorextensions.getservice?view=efcore-10.0) (primary source; retrieved 2026-10-17)
- [EF compiled models and context pooling](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics) (primary source; retrieved 2026-10-17)
- [EF complex query operators](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators) (primary source; retrieved 2026-10-16)
- [EF query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters) (primary source; retrieved 2026-10-16)
- [EF tracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking) (primary source; retrieved 2026-10-16)
- [EF single and split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries) (primary source; retrieved 2026-10-16)
- [EF asynchronous programming](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async) (primary source; retrieved 2026-10-16)
- [EF 10.0.12 aggregate preparation and FirstOrDefault](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.cs) (primary source; retrieved 2026-10-16)
- [EF 10.0.12 navigation expansion](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Query/Internal/NavigationExpandingExpressionVisitor.cs) (primary source; retrieved 2026-10-16)
- [EF 10.0.12 row identity](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs) (primary source; retrieved 2026-10-16)
- [EF 10.0.12 scalar defaults](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalSqlTranslatingExpressionVisitor.cs) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 replacing expression visitor](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/ReplacingExpressionVisitor.cs) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 structural equality translation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalSqlTranslatingExpressionVisitor.StructuralEquality.cs) (primary source; retrieved 2026-10-16)
- [EF execution strategies and transactions](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency#execution-strategies-and-transactions) (primary source; retrieved 2026-10-16)
- [PostgreSQL 17 multicolumn B-tree indexes](https://www.postgresql.org/docs/17/indexes-multicolumn.html) (primary source; retrieved 2026-10-16)
- [PostgreSQL 17 EXPLAIN instrumentation](https://www.postgresql.org/docs/17/sql-explain.html) (primary source; retrieved 2026-10-16)
- [PostgreSQL 17 subquery pull-up](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/backend/optimizer/prep/prepjointree.c) (primary source; retrieved 2026-10-16)
- [PostgreSQL 17 expression selectivity](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/backend/utils/adt/selfuncs.c) (primary source; retrieved 2026-10-16)
- [PostgreSQL 17 clause selectivity](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/backend/optimizer/path/clausesel.c) (primary source; retrieved 2026-10-16)
- [PostgreSQL 17 default selectivity constants](https://github.com/postgres/postgres/blob/REL_17_STABLE/src/include/utils/selfuncs.h) (primary source; retrieved 2026-10-16)
- [MySQL 8.4 derived-table optimization](https://dev.mysql.com/doc/refman/8.4/en/derived-table-optimization.html) (primary source; retrieved 2026-10-16)
- [MySQL 8.4 derived-condition pushdown](https://dev.mysql.com/doc/refman/8.4/en/derived-condition-pushdown-optimization.html) (primary source; retrieved 2026-10-16)
- [MariaDB 11.8.9 derived-table merge eligibility](https://github.com/MariaDB/server/blob/mariadb-11.8.9/sql/sql_lex.cc#L4184-L4216) (primary source; retrieved 2026-10-16)
- [MySQL 8.4.11 execution constants](https://github.com/mysql/mysql-server/blob/mysql-8.4.11/sql/item.h#L2399-L2413) (primary source; retrieved 2026-10-16)
- [MySQL 8.4.11 range comparison dependencies](https://github.com/mysql/mysql-server/blob/mysql-8.4.11/sql/range_optimizer/range_analysis.cc#L1088-L1118) (primary source; retrieved 2026-10-16)
- [MariaDB 11.8.9 correlated-reference dependencies](https://github.com/MariaDB/server/blob/mariadb-11.8.9/sql/item_subselect.cc#L470-L488) (primary source; retrieved 2026-10-16)
- [MariaDB 11.8.9 range comparison dependencies](https://github.com/MariaDB/server/blob/mariadb-11.8.9/sql/opt_range.cc#L9280-L9307) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 distinct navigation expansion](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/NavigationExpandingExpressionVisitor.cs#L1003-L1014) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 scalar aggregate navigation expansion](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/NavigationExpandingExpressionVisitor.cs#L894-L906) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 aggregate preparation before selector translation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.cs#L2218-L2242) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 distinct aggregate pushdown](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs#L4108-L4116) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 distinct projection pruning](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/SqlTreePruner.cs#L175-L180) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 set-operation row identity](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs#L2095-L2263) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 join row identity](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/SqlExpressions/SelectExpression.cs#L3297-L3323) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 scalar cardinality reduction](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore/Query/Internal/NavigationExpandingExpressionVisitor.ExpressionVisitors.cs#L993-L1024) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 FirstOrDefault limit](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.cs#L704-L732) (primary source; retrieved 2026-10-16)
- [EF 10.0.13 absent scalar default](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalSqlTranslatingExpressionVisitor.cs#L589-L600) (primary source; retrieved 2026-10-16)
