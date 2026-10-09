# Support and qualification

This document owns the tested runtime, provider, engine, migration, and tooling
matrix. Package declarations define what can restore; qualification evidence
defines what this repository has exercised.

## Product baseline

| Component | Repository baseline |
| --- | --- |
| Target framework | .NET 10 |
| SDK | `10.0.401`, pinned by `global.json` |
| EF Core runtime range | `10.0.12` through, but excluding, `11.0.0` |
| Core package | `Doka.NestedSet` |
| EF package | `Doka.EntityFrameworkCore.NestedSet` |

The exact dependency graph is locked per project. Updating a package requires
reviewing the lockfiles and rerunning the complete applicable matrix.

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

| Database | EF provider baseline | Engine image baseline |
| --- | --- | --- |
| MySQL | `Doka.EntityFrameworkCore.MySql` 10.4.4 | MySQL 8.4.11 |
| MariaDB | `Doka.EntityFrameworkCore.MySql` 10.4.4 | MariaDB 11.8.9 |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 | PostgreSQL 17.11 |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 | In-process native runtime from the locked package graph |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 | SQL Server 2022 CU27 on Ubuntu 22.04 |

Container image tags and SHA-256 digests are pinned in
[the shared Dockerfile](../docker/database-images.Dockerfile). Digest pinning identifies exact test
content; it does not replace compatibility or vulnerability review.

[Dependabot](../.github/dependabot.yml) proposes maintenance updates within the
qualified MySQL 8.4, MariaDB 11.8, and PostgreSQL 17 lines. MySQL and MariaDB
major and minor changes are excluded; PostgreSQL major changes are excluded
while its minor maintenance releases remain eligible. Adding or replacing an
engine line requires an explicit qualification decision and an updated matrix.

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
matrix. The approved 2022 CU27 baseline still requires native x64 CI evidence;
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

| Database | SafeMigrations adapter baseline |
| --- | --- |
| MySQL/MariaDB | `Doka.EntityFrameworkCore.SafeMigrations.MySql` 10.4.5 |
| PostgreSQL | `Doka.EntityFrameworkCore.SafeMigrations.PostgreSql` 10.4.5 |
| SQLite | `Doka.EntityFrameworkCore.SafeMigrations.Sqlite` 10.4.5 |

SafeMigrations tests are additive. A failure there must not be described as a
runtime requirement for Doka, Npgsql, or SQLite. SQL Server has no optional
SafeMigrations adapter in this repository.

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
databases. Each provider assembly starts at most one container per engine;
concurrent test fixtures receive separate databases on that server. This
keeps xUnit's collection parallelism without multiplying server processes.
The test infrastructure owns its containers and credentials; it does not
depend on a developer-managed database.

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

Docker must be available for the server matrix:

```sh
bash eng/release-candidate.sh \
  --version 10.0.0-rc.1 \
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
