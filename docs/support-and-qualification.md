# Support and qualification

This document owns the tested runtime, provider, engine, migration, and tooling
matrix. Package declarations define what can restore; qualification evidence
defines what this repository has exercised.

## Product baseline

| Component | Repository baseline |
| --- | --- |
| Target framework | .NET 10 |
| SDK | `10.0.403`, pinned by `global.json`; includes runtime `10.0.14` |
| Common EF Core declared range | `10.0.12` through, but excluding, `11.0.0`; adapter dependencies may narrow it |
| EF Core qualification patch | `10.0.14` |
| Test SDK and Code Coverage qualification | `18.11.0` |
| Property-based test integration | `FsCheck.Xunit.v3` `3.6.0` |
| Core package | `Doka.NestedSet` |
| Common EF package | `Doka.EntityFrameworkCore.NestedSet` |
| Native adapter packages | `.MySql`, `.Postgres`, `.Sqlite`, `.SqlServer` under `Doka.EntityFrameworkCore.NestedSet` |

The SDK pin includes Microsoft's
[out-of-band .NET 10.0.14 security update](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.14/10.0.14.md).
Current declarations and dated execution evidence remain separate below.

Code Coverage 18.11.0 remains the approved tooling baseline with the mandatory
uninstrumented Unit allocation/GC-lifetime partition recorded in
[D-013](decisions/D-013-provider-owned-integration-test-projects.md). Its injected
event registrations and active flush worker prevent collectible-module unloading
in the diagnosed macOS ARM64 static-instrumentation path. The partition preserves
ordinary NestedSet lifetime assertions; it does not fix the collector or qualify
unloading under instrumentation. The
[control matrix, execution procedure and re-evaluation criteria](performance.md#coverage-collector-18110-and-unloadability)
define the boundary; this tooling choice changes no shipping dependency or
supported product contract.

The six shipping package graphs are locked. Tests, samples, and tooling use
the centrally reviewed dependency versions without additional committed
lockfiles. Updating a package requires reviewing affected locks and rerunning
the complete applicable matrix.

Non-shipping projects use NuGet's
[transitive pinning](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management#transitive-pinning)
to raise the existing SQL Client PKCS dependency to 10.0.14. Its requested 9.0.11
version triggers the active vulnerability audit. This qualification policy is
separate from shipping dependency declarations. The ORM-independent and common
EF packages have no SQL Client dependency; the SQL Server adapter's native graph
is reviewed and inspected as its own shipping dependency closure. Its explicit
`System.Security.Cryptography.Pkcs` minimum `9.0.21`, without an upper bound,
supplies the native SQL Client security floor without promoting qualification-only pinning into
other shipping packages. Shipping lockfiles record each package's declared
supported minimum.

PKCS 9.0.21 is the first patched 9.x version for Microsoft's
[CVE-2026-70162](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7)
and [CVE-2026-84008](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx)
advisories. The 10.0.14 qualification pin selects the current .NET 10 package, not an
additional consumer security minimum. Applications can retain 9.0.21 or select
a newer compatible version without a dependency downgrade diagnostic.

Every adapter declares an exact dependency on its matching common EF kernel
release because that integration uses internal members. The kernel's public
dependency on `Doka.NestedSet` remains a minimum. Package inspection checks the
actual primary and symbol archives, not just the source declarations. The SDK
also validates the two original packages against their published `10.0.0`
baseline; the approved removal of the former options-registration API is
recorded by a narrowly scoped compatibility suppression. The four new adapter
IDs have no preceding package to use as a baseline.

Version `10.0.0` is the initial stable contract. The `10.1.0` source includes
generic query composition and the explicitly breaking provider-package/callback
registration transition described in the [upgrade guide](upgrading-provider-packages.md).
The owner explicitly kept `10.1.0` for this narrow SemVer exception; package
majors follow the .NET family and `11.x` is reserved for .NET 11. Other breaking
minor changes are not authorized by that decision. Qualification
always identifies its exact source and artifacts. The dated local results
below describe their own runs; hosted release evidence belongs to the matching
release and is not inferred from a successful local execution.

`Doka.EntityFrameworkCore.NestedSet` 10.x does not support `PublishTrimmed` or
`PublishAot`. Runtime model-based generic construction and dynamically
composed EF queries are not trim- or NativeAOT-qualified. EF compiled-model
tests exercise normal .NET execution and do not establish NativeAOT support.
This statement does not assert the same limitation for the separate
`Doka.NestedSet` package. See the official
[.NET library trimming guidance][trim-guidance] and
[EF Core NativeAOT limitations][ef-aot].

[trim-guidance]: https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming
[ef-aot]: https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries

## Provider and engine matrix

Install one NestedSet adapter for each context's native provider:

| NestedSet adapter | Direct native provider range | Effective EF constraint |
| --- | --- | --- |
| `.MySql` | Doka `[10.6.2,10.7.0)` | Common EF minimum plus Doka's EF Relational `<10.1.0` bound |
| `.Postgres` | Npgsql `[10.0.3,11.0.0)` | Common EF range intersected with the selected Npgsql version |
| `.Sqlite` | Microsoft SQLite `[10.0.13,11.0.0)` | EF/provider 10, at least `10.0.13` |
| `.SqlServer` | Microsoft SQL Server `[10.0.13,11.0.0)` | EF/provider 10, at least `10.0.13` |

Suffixes use the `Doka.EntityFrameworkCore.NestedSet` prefix. These declared
ranges do not establish qualification of every future native-provider patch.
The exact executed graph is identified by each qualification record below.

| Database | EF provider baseline | Engine image baseline |
| --- | --- | --- |
| MySQL | `Doka.EntityFrameworkCore.MySql` 10.6.2 | MySQL 8.4.13 |
| MariaDB | `Doka.EntityFrameworkCore.MySql` 10.6.2 | MariaDB 11.8.9 |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 | PostgreSQL 17.11 |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` 10.0.14 | In-process native runtime from the resolved package graph |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` 10.0.14 | SQL Server 2022 CU27-GDR1 on Ubuntu 22.04 |

Container image tags and SHA-256 digests are pinned in
[the shared Dockerfile](../docker/database-images.Dockerfile). Digest pinning identifies exact test
content; it does not replace compatibility or vulnerability review.

The 2026-10-26 maintenance selection keeps MySQL 8.4, MariaDB 11.8,
PostgreSQL 17, and SQL Server 2022. MariaDB and PostgreSQL use refreshed
digests at their existing tags. Earlier measurements retain their original
engine, collector, and dependency identities; changing these inputs does not
relabel those results as qualification of the refreshed artifacts.

These are the current declared qualification inputs. The
[2026-10-26 six-package local record](qualification/provider-migrations.md#six-package-local-qualification-2026-10-26)
identifies the final provider-split working source, all nine executable suites,
and archive, consumer, SBOM, sample and coverage checks. It reports 11,789 passed
cases, zero failures and 2,081 explicit native SQL Server ARM skips; its MySQL
counter-refinement recheck remains distinct from the full suite.
That dated record used SDK `10.0.401` and EF `10.0.13`; it does not establish
qualification of the subsequent servicing update or measurement partition.

The 2026-10-25 update to published Doka 10.6.2 and SafeMigrations 10.6.0 closed
the earlier pending pin transition. The
[2026-10-25 published runtime record](qualification/provider-migrations.md#published-runtime-qualification-2026-10-25)
reports 11,726 passed cases, zero failures, and 2,072 existing native-SQL-Server
ARM exclusions across nine projects. It remains separate from dated private
builds and does not qualify native SQL Server CI.

[Dependabot](../.github/dependabot.yml) proposes maintenance updates within the
qualified MySQL 8.4, MariaDB 11.8, and PostgreSQL 17 lines. MySQL and MariaDB
major and minor changes are excluded; PostgreSQL major changes are excluded
while its minor maintenance releases remain eligible. Adding or replacing an
engine line requires an explicit qualification decision and an updated matrix.

Coupled Doka provider/adapter releases share one Dependabot group. The EF Core
group includes the root package and its provider/tooling packages. Existing
runtime compatibility ranges and engine lines are separate contracts; updating
qualification packages does not expand them. Doka 10.5.0's opt-in native
MariaDB UUID mode is not newly qualified by this dependency refresh.

The [2026-10-23 source comparison](qualification/provider-migrations.md#doka-mapping-compatibility)
separately tests published Doka 10.6.1 and a fresh package from its more recent
local branch. Those frozen package identities remain separate from the current
baselines above. The required engine,
source-binding and ordered-join APIs are now published. That earlier comparison
identified an entity-split GUID convention regression. The later
[recorded local qualification](qualification/provider-migrations.md#current-local-adapter-contract)
uses Doka's repaired local source and passes all 3,843 ordinary MySQL/MariaDB
cases, including the 114 formerly failing split-mapping cases. This does not
qualify the newer published graph or SQL Server database execution on ARM.

SQL Server container images are supported by Microsoft only on Linux x86-64
hosts. Emulation or translation on Arm hosts is not a qualified substitute for
the SQL Server integration matrix, even if a local run succeeds. See Microsoft's
[SQL Server container support note](https://learn.microsoft.com/en-us/sql/linux/containers/deploy?view=sql-server-ver17).

The 2026-10-09 policy keeps SQL Server database cases visible on local ARM
machines and skips them at runtime before container startup. It applies to the
SQL Server provider project and SQL Server cases in the ordinary migration and
SafeMigrations projects. Metadata and unit cases, including SQL Server model
and platform-policy checks, remain runnable; other engines retain their cases.
The environment decision does not catch or suppress container startup or SQL
failures on an eligible platform.

Native Linux x64 CI is the required SQL Server qualification environment. All
applicable SQL Server cases remain mandatory with zero skips; unsupported CI
platforms fail instead of skipping. The SQL Server CI guard fails on unsupported hosts; release-candidate
qualification rejects skipped cases. A local ARM run therefore cannot qualify the complete
matrix. The selected 2022 CU27-GDR1 baseline still requires native x64 CI evidence;
the image change does not itself establish successful qualification or a vendor
correction for the earlier 2025 CU9 startup failures.

The private candidate assessment preceding this policy recorded 1,072 SQL
Server provider cases, 10 ordinary SQL Server migration cases, 51 SQL Server
SafeMigrations cases, and 30 startup attempts passing under macOS ARM x86-64
emulation. Those results support only that candidate and local environment;
they do not qualify native CI or establish Microsoft support for emulation.
[D-014](decisions/D-014-shared-database-images-and-developer-compose.md)
retains the image decision and the unresolved CU9 crash history, including the
native GitHub-hosted x64 report.

Pomelo is not a supported provider. MySQL and MariaDB support is qualified only
with Doka.

## Migration matrix

Ordinary EF migrations are required and tested for all five engine profiles.
They must work without SafeMigrations.

Optional adapter qualification uses:

| Database | SafeMigrations adapter baseline | Qualification boundary |
| --- | --- | --- |
| MySQL/MariaDB | `Doka.EntityFrameworkCore.SafeMigrations.MySql` 10.6.0 | Optional adapter regression suite |
| PostgreSQL | `Doka.EntityFrameworkCore.SafeMigrations.PostgreSql` 10.6.0 | Canonical null-filter indexes, replay, and drift |
| SQL Server | `Doka.EntityFrameworkCore.SafeMigrations.SqlServer` 10.6.0 | `dbo`/explicit schemas, integer widening, indexes and CHECKs |
| SQLite | `Doka.EntityFrameworkCore.SafeMigrations.Sqlite` 10.6.0 | Optional adapter regression suite |

SafeMigrations tests are additive. A failure there must not be described as a
runtime requirement for Doka, Npgsql, SQLite, or SQL Server. Adapter packages
are referenced only by the optional test project; no shipping NestedSet package
depends on SafeMigrations.

Published MySQL adapter 10.6.0 requires Doka [10.6.2,10.7.0), matching the
declared 10.6.2 provider. Core and all four adapters use 10.6.0. The earlier
10.5.0 adapter range [10.5.0,10.6.0) did exclude 10.6.1; that historical
compatibility restriction no longer describes the current pins.
The [provider/migration qualification](qualification/provider-migrations.md)
records the later repaired, same-source local graph and actual migrations,
physical schema, data, history and guard-level replay. Only the new adapter
contract remains in tests, now against the declared published graph. There is
no legacy mode, runtime skip or weakened assertion to qualify that contract
against an older graph. Fresh published-package tests passed the available
local scope, including 96 optional adapter cases; 54 native SQL Server adapter
cases remain explicitly excluded on ARM. Dated local evidence and native SQL
Server remain separate boundaries.

The PostgreSQL adapter contract retains recognition of EF-generated canonical
provider-delimited single-column `IS NULL` and `IS NOT NULL` index predicates.
The authored raw SQL and immutable definition remain intact; generation,
preflight, catalog comparison, replay, and drift rejection are exercised by the
consumer regressions. See the [migration contract](migrations.md#provider-matrix).

SQL Server coverage includes the default `dbo` and explicit schemas, package tooling
registration, absent-index application, replay, wrong columns and directions,
and pending-upgrade drift without advancing history. Unqualified safe operations
require the database user's default schema to be `dbo`; otherwise configure an
explicit schema in the EF model. Generated integer CHECKs can validate existing
rows; valid predicates are `Missing`/`Apply`, while a violating row is
`DataBlocked`. Stamped, enabled, trusted checks replay on populated tables;
disabled or enabled-but-untrusted checks are rejected without repair.
The adapter's `RepairIfSafe` contract admits proven historical `int`-to-`bigint`
coordinate widening, including the preceding dependency drops in the real
scaffolded upgrade. Empty and valid populated fixtures retain their data and
gain Int64 capacity. A new CHECK rejected for invalid legacy coordinates rolls
back the full upgrade, preserving original column types, rows, indexes,
constraints, and migration history. Narrowing back to Int32 remains refused
for both fitting and oversized values. Native SQL Server qualification follows the platform policy above. Historical
local emulation results are not a replacement for supported Linux x64 execution.
The [provider/migration summary](qualification/provider-migrations.md)
keeps package/source provenance and actual executed/excluded counts separate
from these required behaviors. The ordinary migration path remains independent
of every optional adapter.

## Test ownership

| Project/area | Evidence |
| --- | --- |
| `Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests` | Benchmark launcher, fixture lifecycle, scenario effects, diagnostics, and provenance |
| `Doka.NestedSet.Tests` | Bounds validation and in-memory relationship semantics |
| `Doka.EntityFrameworkCore.NestedSet.Unit.Tests` | Mapping, guards, provider capabilities, snapshots, pure planning |
| `Doka.EntityFrameworkCore.NestedSet.MySql.Tests` | MySQL/MariaDB queries, mutations, ordering, locking, rollback, diagnostics, indexes |
| `Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests` | PostgreSQL equivalents |
| `Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests` | SQL Server equivalents |
| `Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests` | SQLite equivalents |
| `Doka.EntityFrameworkCore.NestedSet.Migrations.Tests` | Ordinary migration lifecycle and physical catalog |
| `Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests` | Optional adapter generation, execution, rejection, replay, and drift behavior |
| Package consumers | Restores and executes exact produced archives without project references |
| Sample | File-system, KPI, and user-group domain composition |

Integration tests use Testcontainers for server engines and isolated SQLite
databases. Each eligible provider assembly starts at most one container per engine;
concurrent test fixtures receive separate databases on that server. This
keeps xUnit's collection parallelism without multiplying server processes.
The test infrastructure owns its containers and credentials; it does not
depend on a developer-managed database.

MySQL/MariaDB database teardown disposes its context before asynchronously
clearing the exact connection pool owned by that database. Unique fixture
database names must not accumulate idle sessions against the shared server's
connection limit. Initialization and deletion failures retain their original
errors; cleanup does not clear another database's active or idle pool.

The optional [developer Compose environment](../docker/README.md) builds the
same vendor bases into separate local service images for manual debugging and
Rider connections. Testcontainers consumes the pinned vendor references directly;
its generated ports, credentials, container cleanup, and fixture isolation remain
independent. Developer volume contents and a successful Compose startup do not
qualify the runtime or migration matrix.

## Required behavioral coverage

Qualification covers:

- repeated bounds in different scopes without query or mutation leakage;
- multiple TreeIds in one scope with repeated local bounds and
  `TreeContaining` from an arbitrary descendant;
- composable upward and downward payload filters;
- every insert, move, delete, bulk, validate, and rebuild path;
- generated and assigned keys plus native provider key types;
- strict and manual-override ordering, including async SaveChanges reorder;
- caller-owned transactions, savepoints, rollback, cancellation, and unknown
  commit outcomes;
- same-tree serialization and independent TreeId locks within one scope;
- Doka MySQL/MariaDB restrictive self-reference behavior;
- PostgreSQL, SQLite, and SQL Server lock behavior;
- ordinary and optional migration lifecycles;
- active, tombstoned, damaged, purged, and deliberately reused TreeId lifecycles;
- physical structural indexes and stable provider query-plan assertions;
- bounded diagnostics without application data; and
- exact primary/symbol package contents, XML docs, source metadata, SBOMs, and
  isolated consumers.

## Deterministic performance evidence

Automated qualification asserts structural outcomes, SQL command/update counts,
affected-row formulas, and stable query-plan properties. It does not use wall-
clock time, runner CPU, working set, or process-wide allocation as a CI or
release pass/fail threshold.

The benchmark executable is local diagnostic tooling. Results from different
GitHub-hosted runner hardware are not comparable release evidence. See
[Performance and capacity](performance.md).

## Local verification levels

### Fast source and unit feedback

```sh
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx --locked-mode
dotnet build Doka.EntityFrameworkCore.NestedSet.slnx -c Release --no-restore
dotnet test tests/Doka.NestedSet.Tests/Doka.NestedSet.Tests.csproj \
  -c Release --no-build --no-restore
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests.csproj \
  -c Release --no-build --no-restore
```

### Complete repository qualification

The current SafeMigrations test contract uses the declared published Doka
10.6.2 and SafeMigrations 10.6.0 graph documented in
[provider qualification](qualification/provider-migrations.md). The earlier
pin transition is resolved. The available local public-graph scope was repeated
for the six-package working source on
[2026-10-26](qualification/provider-migrations.md#six-package-local-qualification-2026-10-26).
Completing the native SQL Server scope still requires x64 CI.
Do not skip cases or restore old rejection expectations to replace that
qualification.

Docker and supported Linux x64 execution must be available for the complete
server matrix. Local ARM skips provide development feedback only:

```sh
bash eng/release-candidate.sh \
  --version 10.1.0 \
  --workspace \
  --output artifacts/qualification-local
```

This executes the complete release candidate checks locally. Pull-request CI
uses independent source-quality, engineering, build/package, test-project, and
SQLite-sample jobs. Every executable test project has its own named matrix
cell; MySQL and MariaDB share their provider project. Cells can run concurrently
on isolated runners. Coverage is verified across the complete test reports,
and `Repository qualification` requires every gate to succeed. A workspace run
is never publishable.

### Focused projects

```sh
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests.csproj \
  -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests.csproj \
  -c Release
```

Do not use a filtered run as complete qualification. The release runner rejects
inherited VSTest filters.

## Evidence boundaries

A green local run proves only the checked source and local environment. It does
not prove branch protection, GitHub environment approval, OIDC identity,
attestation availability, NuGet publication, repository signing, or public
readback.

A green hosted run proves only its exact source, dependency locks, run attempt,
candidate bytes, and retained checks. It does not establish application schema,
authorization, workload capacity, backup quality, or safe operation of direct
SQL writers.

Every release claim must identify the exact run, attempt, source SHA, candidate
manifest, package hashes, and public verification result. See
[Release process](release-process.md).

## Adding or updating support

A provider or engine line is supported only after:

1. official provider compatibility is verified;
2. the dependency and container identities are pinned;
3. provider capabilities and lock behavior are implemented;
4. runtime, concurrency, migration, index, and query-plan tests pass;
5. package and documentation contracts are updated; and
6. a release candidate completes the same matrix.

Do not infer support from a shared SQL dialect, successful compilation, or an
unqualified provider package restore.
