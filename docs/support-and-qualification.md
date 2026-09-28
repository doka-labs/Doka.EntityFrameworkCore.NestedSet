# Support and qualification

This document owns the tested runtime, provider, engine, and migration
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

| Database | EF provider baseline | Qualified engine image |
| --- | --- | --- |
| MySQL | `Doka.EntityFrameworkCore.MySql` 10.4.4 | MySQL 8.4.11 |
| MariaDB | `Doka.EntityFrameworkCore.MySql` 10.4.4 | MariaDB 11.8.9 |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 | PostgreSQL 17.11 |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 | In-process native runtime from the locked package graph |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 | SQL Server 2025 CU9 |

Container image tags and SHA-256 digests are pinned in
`tests/Shared/test-images.Dockerfile`. Digest pinning identifies exact test
content; it does not replace compatibility or vulnerability review.

SQL Server container images are supported by Microsoft only on Linux x86-64
hosts. Emulation or translation on Arm hosts is not a qualified substitute for
the SQL Server integration matrix, even if a local run succeeds. Run that
matrix on a supported host when collecting release evidence. See Microsoft's
[SQL Server container support note](https://learn.microsoft.com/en-us/sql/linux/containers/deploy?view=sql-server-ver17).

Pomelo is not a supported provider. MySQL and MariaDB support is qualified only
with Doka.

## Migration matrix

Ordinary migrations are required. Their lifecycle and optional SafeMigrations
adapters are qualified by separate migration test projects.

## Test ownership

| Project/area | Evidence |
| --- | --- |
| `Doka.NestedSet.Tests` | Bounds validation and in-memory relationship semantics |
| `Doka.EntityFrameworkCore.NestedSet.Unit.Tests` | Mapping, guards, provider capabilities, snapshots, pure planning |
| `Doka.EntityFrameworkCore.NestedSet.MySql.Tests` | MySQL/MariaDB queries, mutations, ordering, locking, rollback, diagnostics, indexes |
| `Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests` | PostgreSQL equivalents |
| `Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests` | SQL Server equivalents |
| `Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests` | SQLite equivalents |

Integration tests use Testcontainers for server engines and isolated SQLite
databases. Each provider assembly starts at most one container per engine;
concurrent test fixtures receive separate databases on that server. This
keeps xUnit's collection parallelism without multiplying server processes.
The test infrastructure owns its containers and credentials; it does not
depend on a developer-managed database.

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
- public API and XML documentation consistency.

## Deterministic performance evidence

Automated qualification asserts structural outcomes, SQL command/update counts,
affected-row formulas, and stable query-plan properties. It does not use wall-
clock time, runner CPU, working set, or process-wide allocation as a CI or
release pass/fail threshold.

Measurements from different hardware are not comparable performance
evidence. See [Performance and capacity](performance.md).

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

### Focused projects

```sh
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests.csproj -c Release
dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests.csproj -c Release
```

Do not use a filtered run as complete qualification.

## Evidence boundaries

A green local run proves only the checked source and local environment. It does
not prove branch protection, GitHub environment approval, OIDC identity,
attestation availability, NuGet publication, repository signing, or public
readback.

## Adding or updating support

A provider or engine line is supported only after:

1. official provider compatibility is verified;
2. the dependency and container identities are pinned;
3. provider capabilities and lock behavior are implemented;
4. runtime, concurrency, migration, index, and query-plan tests pass;
5. package and documentation contracts are updated; and
6. the complete applicable matrix has current execution evidence.

Do not infer support from a shared SQL dialect, successful compilation, or an
unqualified provider package restore.
