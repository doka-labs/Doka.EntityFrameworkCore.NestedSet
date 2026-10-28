# Query-composition qualification

The dated measurements and rejected candidates below describe their original
provider-binding implementation. The current MySql package binds the published
APIs directly under [D-020](../decisions/D-020-provider-owned-runtime-packages.md).
That ownership change does not replace the recorded native evidence or establish
new timing, allocation, or optimizer results by itself.

This page summarizes the structural and native-work evidence for generic
query composition and the optional ancestor access path. It records dated
local observations, not a release qualification or a universal latency claim.
The [configuration matrix](../configuration.md#current-query-capabilities)
owns current eligibility; the [query guide](../query-composition.md) owns API
semantics. [D-018](../decisions/D-018-generic-hierarchy-query-composition.md) and
[D-019](../decisions/D-019-optional-ancestor-index-and-source-binding.md)
record the architectural decisions.

## CI correction qualification 2026-10-28

The first hosted run of stable preparation, CI `40668521125` at source
`7f770a3e4d9e4878dc518c28a4587ff85b119f6f`, failed one MySQL case, one
PostgreSQL case and 16 SQL Server cases. Its source-quality, build/package,
engineering, Unit, SQLite, sample and migration lanes passed. These corrections
are uncommitted successor changes; they are not a successful hosted rerun.

The failures have distinct evidence boundaries:

- Four SQL Server capacity failures stopped in the shared fixture's `Math.Min`
  translation at EF's default compatibility level 150. Equivalent bounded
  `CASE` arithmetic keeps that default. SQL translation and native SQLite
  full/partial-branch controls pass without changing provider compatibility.
- Four indexed SignalLab failures lacked the SQL Server snapshot prerequisite.
  Indexed, normal and isolated contexts now share the fixture setup and active
  entity observer. A separate control deliberately materializes one entity to
  prove the observer detects it.
- Two SQL Server cancellation assertions required the exception to retain the
  caller token. SqlClient 6.1.7 can cancel its task without that exception
  metadata. Tests still require cancellation and no successful result; the
  original token is checked directly at command/read boundaries.
- The ten-million-node SQL Server case timed out during setup. Its earlier
  attribution to a payload `INSERT SELECT` was incorrect: the exact stack and
  source line identify table-wide `UPDATE STATISTICS ... WITH FULLSCAN`.
  This case seeds only 100 facts. Payload seeding separately uses 100,000-key
  windows with four SQLite key-total controls; those controls do not qualify
  statistics preparation or native SQL Server capacity execution.
- Five SQL Server capacity reads timed out in actual composed queries. Retained
  wide plans read about 9.5-10 million index/lookup rows for selective requests.
  The initial strict ancestor coordinate relation and scalar parent-existence
  proof passed local translation and semantic controls. Successor CI shows that
  coordinate-only hydration still repeats large scans; it is not a qualified
  physical fix. The successor candidate uses conditional native owner keys and
  a single projected nearest decision. Native x64 capacity execution and actual
  plans remain outstanding, including the deep repeated child scans.
- The MySQL query returned all 501,005 expected rows, but the separate
  instrumented replay was interrupted after about 30 seconds and returned a
  partial root of 470,220 rows. The replay-only watchdog and warning checks
  described in the [performance guide](../performance.md#regression-diagnosis)
  preserve the original result and work assertions. The corrected original
  native MySQL case passes with complete root cardinality 501,005, 604,007
  base-table tuples against the unchanged 32,128,384 allowance, and 100,815,239
  operator tuples against 400,004,000. Two native replay controls independently
  verify complete execution and rejection of an explicitly interrupted replay.
- The PostgreSQL 10,000-node `Path` timeout was not reproduced. All eight
  original deep profiles pass both plain and with the CI coverage settings on
  the predecessor binary. The successor's full 58-case composition suite also
  passes. Original and successor path plans observe 30,282 and 30,281 shared
  buffer hits respectively, with one scalar result and no entity materialization.
  These local observations neither explain the hosted timeout nor establish a
  speedup. The original failed query's SQL/estimated plan is now retained if it
  recurs, including when diagnostic plan capture also fails.

The portable long-path changes additionally pass 36 mapping cases on native
PostgreSQL and SQLite each: complete and interrupted paths with independent,
row-correlated and nearest rules over shadow, named-indexer, TPT, binary,
converted and entity-split models. A focused 663-case Unit composition cohort
passes. Native SQL Server execution remains required for the changed SQL
Server strategy, snapshot setup and large workloads. No capacity case is
excluded, and no semantic, allocation or native-work budget is increased.

The final local Release solution build completes with zero warnings/errors;
Roslyn style and IDE0005 verify-only checks pass. The final Unit partitions
pass 2,318 ordinary and 451 uninstrumented allocation cases. The targeted
native MySQL/MariaDB cohort passes 114 mapping, cancellation, deep-path and
indexed-report cases; PostgreSQL passes 17 deep-path, cancellation and indexed
report cases with Code Coverage 18.11.0 and the CI runsettings. These are
focused cohorts, not complete native provider suites. The separate original
MySQL failure passes as described above. All quoted local cohorts have zero
failures/skips; none is native SQL Server or hosted release evidence.

### Successor CI and PostgreSQL broad-payload correction

Successor CI `40684866314` at `3488d2149ac85e42d0c6e8b91c890961a98fec61`
passes the corrected setup/cancellation contracts but exposes a PostgreSQL
100,000-node broad-payload failure and eight remaining SQL Server capacity
failures. These results supersede assumptions about optimizer behavior, not
the successful local semantic controls above.

The PostgreSQL case is reproduced locally without coverage or a timeout
change. The successful smaller 10,000-node profile already reads 50,000,000
materialized fact tuples and rejects 49,995,000 join pairs. The 100,000-node
version times out in the original reader. Its inactive nearest-owner branches
contain two depth-ordered key selections and another mapped-owner lookup;
their high estimated cost affects the surrounding payload join even when
those branches do not execute.

The corrected PostgreSQL strategy reuses the existing projected decision
primitive, selecting the nearest owner once with `Depth DESC`. Candidate
selection still precedes value and complete-path decisions. MySQL/MariaDB and
SQLite retain their existing projected Left ordering. Native positive and
negative checks retain row-aware criteria, missing paths, rejected nearest
owners, self rejection and source occurrences. Both original Medium cases
pass, alongside the dedicated native work guards and broader Unit controls.

| PostgreSQL profile | Maximum emitted tuples | Rejected join tuples | Existing operator budget |
| --- | --- | --- | --- |
| 10,000 nodes, one anchor, original | 50,000,000 | 49,995,000 | 40,002 |
| 10,000 nodes, one anchor, corrected | 10,000 | 0 | 40,002 |
| 100,000 nodes, one anchor, corrected | 100,000 | 0 | 400,002 |
| 100,000 nodes, 1,000 anchors, corrected | 101,000 | 0 | 603,998 |

The independent SQL comparison uses an owned PostgreSQL 17.11 container with
two CPUs and 2 GiB. Both corrected reads return exact fact counts and key sums;
the original 100,000-node read reaches the unchanged 30-second timeout. No
planner setting, alternate consumer query, relation boundary, index or
duration gate is introduced. The
[work controls](../../tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests/Querying/Composition/NearestDecisionWorkTests.cs)
use the existing actual-plan parser and work budgets; a negative Unit control
rejects the five-billion-tuple extrapolated bad shape with that same budget.

The CI25 SQL Server source still reads population-sized owner/child ranges per
source in successful neighboring profiles. The initial coordinate-only
`DISTINCT` shape does not establish the intended materialization order and is
not a qualified performance fix. Those native failures remain open until
their corrected source passes supported x64 execution with actual plans.

The initial SQL Server candidate preserved `Depth DESC` while projecting its
nearest-owner decision once. The now-unused scalar-key route and exclusive
method templates were removed; all four adapters explicitly select one of the
two consumed depth/left orderings. Its ancestor relation retains nonleaf
eligibility inside a conditional native-key projection and hydrates by key/tree
instead of three bound coordinates. Fifteen SQL-translation and executed
portable semantic controls pass, as do 682 broader Unit/provider controls.
The final cleaned core also passes 22 PostgreSQL cases including both original
Medium populations and native work guards. These results do not establish the
initial SQL Server candidate's physical plan or bound the deep child scans.

The ten-million-node setup correction enumerates the case-owned table's real
`sys.stats` entries and updates each one with `FULLSCAN`, retaining the ordinary
command deadline. Thirty replay/cleanup/statistics controls pass, including
bound object identity, escaped identifiers, default and explicit deadlines,
and catalog/read/update failures. An individual scan and the full native
ten-million-node case remain subject to x64 verification.

### SQL Server follow-up corrections

The independent owner relation also collapses excluded coordinates, not only
their key: `(NULL, Tree, 0, 0)` represents all excluded intervals of one tree.
Eligible owners retain their native identity and original bounds. Direct counts
of the actual production relation fail before this change at 101/202 classes
for one/two trees, then pass at two/four classes with the same 100 leaves per
tree. Existing zero-key, visibility, converted-identity, extreme-bound and
equal-bound-owner semantic controls remain required. This reduction is an
executed logical-cardinality result, not a native SQL Server seek guarantee.

The follow-up replaces independent-rule long-path child counts with shared
invalid-child summaries. A relational singleton computes adaptive width before
grouping; summary keys and `MAX` operands contain local columns only. Separate
existence predicates apply the outer path limits. Row-dependent rules retain
their exact count and native parent comparison. MySQL/MariaDB retain their
existing scalar-width strategy. The
[direct helper controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition/SqlServerAncestorBoundsTests.RelationalPaths.cs)
execute the new classifier and relation on SQLite with scoped/unscoped,
converted, hidden/missing parent, blocked edge and partition-boundary cases.

SQL Server now uses the existing descending-`Left` nearest-owner strategy.
Valid ancestor intervals form one nested chain, so their depth order is the
same coordinate order even with nonmonotonic keys or filtered candidates.
Nearest-before-value/path rejection is unchanged. This addresses the separate
depth-sort expression still present in the initial single-projection candidate;
PostgreSQL retains its independently measured depth strategy.

The UserGroups consumer reads missing/ambiguous markers independently from
complete unique-person evidence. Evidence retains its `DISTINCT` boundary and
joins the grouped unique-person witnesses through an inner join. The status
and evidence branches use one `UNION ALL` reader with unchanged physical row
budgets, complete occurrences and per-origin isolation.

The existing mixed, deep and supervisor capacity profiles additionally inspect
SQL Server's cumulative per-thread `ActualRows` and `ActualRowsRead`, without
multiplying by executions. They use the existing `ForPaths` allowance derived
from the population and independent selected adjacency. Five retained adverse
CI25 plans exceed this allowance, including 100,000,000 owner reads against
206,000 permitted tuples and 10,000,100 child reads against 390,302. These are
old-plan counterexamples, not executions of the corrected source. The guard
bounds the largest operator; it is not a total CPU, total I/O or latency limit.

The final local follow-up uses a fresh complete Release build with zero
warnings/errors. All 5,820 selected executions pass without skips: 451
uninstrumented allocation/lifetime controls, 2,422 complementary Unit cases
with Code Coverage, 1,480 native MySQL/MariaDB cases, 735 native PostgreSQL
cases and 732 SQLite cases. The native selections include affected semantic,
mapping and sample contracts; MySQL/MariaDB and PostgreSQL additionally execute
the original medium and large consumer capacity profiles. Style and unused-import
verification pass. All 1,873 captured source/configuration hashes remain unchanged
through these runs. These are local follow-up results, not a hosted CI result.

Generated SQL, portable execution and other native engines cannot establish
SQL Server's selected access path. All eight affected SQL Server capacity
cases and their actual plans still require supported x64 execution of this
source. No command deadline, index, query hint or acceptance budget was raised.

## Semantic and native-work evidence

The public operations preserve source occurrences, exact Scope/TreeId and
named mapping identity, application filters and deferred EF execution.
Strict ancestors exclude self. `Any` proves visible existence; `Nearest`
selects the nearest candidate before testing its value and complete visible
path, with no fallback to a farther matching owner. Complete-path rules reject
missing, hidden or invalid intermediate edges. Both direction orders and
repeated sources are separate regression cases.

The 2026-10-16 pre-hardening cohort used NestedSet DLL SHA-256
`4305bbf0bf95cf34f2578ea5b7cf2bbdd7763201a6051fa83a66cbbcaa869442`,
SDK 10.0.401, EF 10.0.13, Doka 10.5.0 and Npgsql 10.0.3. Its small paired
BenchmarkDotNet cases all allocated more than the explicit-LINQ reference,
and its selective MySQL UNION plan regressed native work. Those results
qualify the earlier implementation only, not the later hardening.

The 2026-10-17 dense-path qualification used EF 10.0.13, Doka 10.5.0,
MySQL 8.4.11 and MariaDB 11.8.9. All 67 focused native controls passed, including
million-node positive/rejection cases. The original 100,000-node proof emitted
2,500,100 iterator tuples from repeated invalid-child scans; the retained
partitioned proof reduced the maximum observed operator output to 100,000.
One-million-node cases observed at most 1,000,000 operator tuples and 250,001
known materialized output tuples. Two MariaDB materialized-output observations
were unknown and were not counted as zero.

These are executed-plan observations for their fixture, not physical I/O or
a proof that every application plan is linear. Current deterministic budgets,
parameters, populations and test owners are in the
[performance guide](../performance.md#deterministic-construction-and-transport-budgets)
and [coverage map](../query-composition-regression-coverage.md).

## Optional ancestor access

The additive nonleaf index uses completed physical roles:

```text
(Scope?, IsNonLeaf, TreeId, Right, Left, NodeKey)
```

Schema availability, strict source binding and inclusive ordered-edge access
are independent capabilities. The optimized path requires the configured
option, the required public Doka APIs and an eligible source. Other providers
or ineligible sources retain the existing query strategy; configuring an
index alone does not establish a read benefit.

The 2026-10-19 frozen experiment used private Doka
`10.5.1-orderedjoin.20261019.1`, EF 10.0.13, MySQL 8.4.11 and MariaDB 11.8.9.
The measured NestedSet DLL SHA-256 was
`f5ce15fcadd295b19d5a1f15f92c1115fee6474173663615a6eca79db464ece7`.
The separate combined-provider consumer acceptance used Doka
`10.5.1-finalconsumer.20261019.2`, DLL SHA-256
`2af5e7af7fcc5f26c9d62b313137f462853532a86df127f23e7387ca25549aa3`.
Those private identities are historical evidence, not downloadable public
package versions or current source identities.

| Observed cost in that fixture | MariaDB | MySQL |
| --- | ---: | ---: |
| Comparable read medians lower with opt-in | 24 of 42 | 21 of 42 |
| Additional index pages | 23% | 36% |
| Import elapsed increase | 6% | 121% |

The adopted indexed native matrix completed 96 profiles, 48 per engine,
with three exact full reads per library/raw variant and completed native plans.
The same-binary no-opt-in matrix fully qualified 84 controls; ten other controls
failed their original reads and two MySQL controls completed reads but retained
truncated secondary plans. No qualified ratio uses those incomplete controls.
Four real tenant/visibility controls, eighteen fresh write/storage cohorts and
twelve paired dirty-coordinate repair observations completed separately.

Sparse deep profiles improved substantially, while indexed balanced and chain
reads could be slower. Chain temporary candidate rereads still approached one
hundred million work steps despite reduced base access. The balanced strict
repair reduced attributed mapped base access from approximately 50 million to
117,690 tuples (217,691 for the explicit-tree MariaDB control), while retaining
49,958,000 temporary comparisons. Those are different work boundaries.
The native fixture records requested occurrences, distinct selected nodes and
completed structural-source cardinality separately. A requested thousand-source
profile does not by itself prove a thousand distinct nodes.

The option therefore remains explicit. Qualify the complete read/write
workload, including index maintenance, storage, construction allocation and
actual source selectivity. Do not infer a benefit from index creation alone.

## Fact selections and consumer filters

The 2026-10-20 correction qualified independently inspected fact roots,
strict/inclusive bags, three-root fact selection and downstream `Any`,
`Contains` and negated-existence filters. The frozen corrected NestedSet DLL
SHA-256 was
`579d49f7ed94daadc81c974a1b55f7682bfa964fed94673f51cfb14aab31c900`;
its private Doka DLL had SHA-256
`2af5e7af7fcc5f26c9d62b313137f462853532a86df127f23e7387ca25549aa3`.

All 64 corrected moderate outcomes passed across both engines, including
640 complete measured reads. Four separate million-node strict/inclusive
controls passed. The earlier baseline retained native-work failures and two
inclusive timeouts. Application fact indexes were identical between paired
cases, but whole-table populations, statistics and cache state were not
identical; elapsed deltas are not causal implementation speedup ratios.

Representative corrected million-node observations were:

| Engine | Strict work / elapsed | Inclusive work / elapsed |
| --- | ---: | ---: |
| MariaDB | 27,029 / 37.5839 ms | 50,046 / 90.2019 ms |
| MySQL | 20,020 / 69.7434 ms | 40,031 / 46.5902 ms |

These were single completed reads, not warmed latency distributions. Moderate
fact-route construction and complete-read allocation increased in several
cases even when native work decreased. Unindexed application fact scans also
occurred in the baseline; a bounded hierarchy plan does not supply missing
application indexes.

## Current semantic regression scope

The 2026-10-24/25 final-product checks passed 2,611 Unit cases and 156 native
MySQL/MariaDB work controls with unchanged budgets. Four generated compiled-model
query/write cases passed; their SQL Server counterpart remains unexecuted on ARM.
The [provider record](provider-migrations.md) identifies exact local packages,
product binary and the distinct broader regression cohorts.

Ordinary captured query/DbSet/property/Set spellings are normalized by EF and
can reach the same qualified fact-selected path. Marked downstream Path/Any/
Nearest filters retain immediate guards and reuse the existing normalized proof
cores on every options-registered provider. Model-only construction retains its
previous strategy. The index reader still needs opt-in schema and Doka APIs.
Compilation-local root sharing does not admit application callbacks or a global
query/context cache.

The inclusive indexed strategy directs both structural candidate matching and
final mapped-owner hydration through the existing ordered-join API, while
source hydration remains ordinary. A prior owner-first native plan preserved
all results but exceeded the unchanged work allowance; the fix constrains that
specific alias dependency rather than increasing the budget or adding a spool.
The final native cohort passes independently of the historical timing tables.
No quiet-host complete-reader latency claim follows from those work controls.

## Reproduce the repository checks

Use the SDK and dependency graph declared for the source under test. For
optional current-source provider features, the selected package graph must
actually contain those features; a published legacy package cannot reproduce
a private-build observation. See the
[provider qualification boundary](provider-migrations.md).

```sh
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~Composition'

dotnet test tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~HierarchyCompositionWork|FullyQualifiedName~AncestorIndexWork|FullyQualifiedName~DensePathWork'
```

The native cases own disposable databases and retain independent result
oracles and executed work counters. MySQL/MariaDB work metrics are not an
interchangeable cross-engine cost unit. The public tests and benchmark
fixtures are the reproducible procedures; these commands are not assertions
that a new run has completed. Complete large-capacity and native SQL Server
qualification remain separate from the focused dated observations.
