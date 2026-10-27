# Query-composition qualification

The dated measurements and rejected candidates below describe their original
provider-binding implementation. The current MySql package binds the published
APIs directly under [D-020](../decisions/D-020-provider-owned-runtime-packages.md).
That ownership change does not replace the recorded native evidence or establish
new timing, allocation, or optimizer results by itself.

## Semantic and native-work evidence

The public operations preserve source occurrences, exact Scope/TreeId and
named mapping identity, application filters and deferred EF execution.
Strict ancestors exclude self. `Any` proves visible existence; `Nearest`
selects the nearest candidate before testing its value and complete visible
path, with no fallback to a farther matching owner. Complete-path rules reject
missing, hidden or invalid intermediate edges. Both direction orders and
repeated sources are separate regression cases.

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

## Reproduce the repository checks

Use the SDK and dependency graph declared for the source under test. For
optional current-source provider features, the selected package graph must
actually contain those features; a published legacy package cannot reproduce
a private-build observation. See the
[provider qualification boundary](provider-migrations.md).


The native cases own disposable databases and retain independent result
oracles and executed work counters. MySQL/MariaDB work metrics are not an
interchangeable cross-engine cost unit. The public tests and benchmark
fixtures are the reproducible procedures; these commands are not assertions
that a new run has completed. Complete large-capacity and native SQL Server
qualification remain separate from the focused dated observations.
