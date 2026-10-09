---
id: D-008
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Provider contracts, structural index ownership, and optional SafeMigrations integration"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-008 -- Keep supporting indexes independent of optional migration adapters

## Context and Problem Statement

Nested-set reads need scope-leading access paths, while structural updates temporarily change many coordinates.
Applications must obtain the required indexes from ordinary EF migrations even when SafeMigrations is absent. MySQL
and MariaDB use the Doka provider; PostgreSQL, SQLite, and SQL Server have their configured provider paths.

## Decision Drivers

- Configure indexes through the final EF model and physical property mappings.
- Avoid unique structural constraints that conflict with interval-shift update sequences.
- Qualify ordinary migrations independently from optional adapter behavior.
- Keep exact tested provider and adapter versions visible in the support matrix.
- Keep PostgreSQL FK principal checks independent of generated tree-local access paths during atomic imports.

## Considered Options

- Provider-neutral model indexes with optional adapter qualification
- Require SafeMigrations for all structural schema creation
- Publish manual provider-specific index SQL only

## Decision Outcome

Chosen option: "Provider-neutral model indexes with optional adapter qualification", because indexes are part of
the entity model, while SafeMigrations is an optional application migration policy. A provider dependency does not
belong in the shipping NestedSet packages.

PostgreSQL specializes index eligibility without changing the structural key
sequences. An untouched library-created structural index receives a mapped
`TreeId IS NOT NULL` predicate. TreeId is required, so this indexes every
hierarchy row. Tree-local equality implies the predicate, whereas self-FK
principal checks constrain only Scope/NodeKey. An untouched conventional
nullable self-FK index receives `Parent IS NOT NULL`; a companion root index
on the principal columns followed by Parent receives `Parent IS NULL`. PK/alternate-key
uniqueness and the FK itself are unchanged. Explicit application metadata is
preserved; no application index is removed to influence a plan.

The trailing Parent keeps the root index physically co-located with its filter
column. EF maps inherited TPT indexes using their key columns, without checking
raw filter references. A principal-only root index would also be emitted on a
payload table without Parent and fail database creation. Including Parent
preserves the leading Scope/NodeKey lookup and excludes such tables; every
indexed root has a null Parent. It also retains the distinct dependent-FK
prefix, so EF's filter-unaware coverage convention does not remove that path.

The PostgreSQL finalizer runs after structural index reconciliation. EF Core
10.0.12 completes each finalizing convention's delayed batch before invoking
the next convention. This makes the conventional self-FK index available for
the subsequent parent specialization. Structural filtering acts only on exact
indexes owned by the current mutable model and still carrying unmodified
convention metadata. Root companion names derive from physical identifiers
and check model and database-name collisions. Other providers keep their
existing definitions.

The reason is observed, rather than a hypothetical optimizer preference.
During a million-child atomic import, default automatic analysis saw no
committed rows and invalidated a cached RI plan. The importing transaction
still saw its growing rowset. A scope-only scan then replaced the composite
principal-key lookup for each new child. Filtering only the dependent Parent
index still allowed a structural Scope/TreeId/Right index to be selected.
TreeId predicates exclude those generated paths from the principal check.
Regression plans cover both native and converted TreeIds and public tree
queries. Actual post-maintenance RI evidence remains a separate capacity
qualification; a synthetic EXPLAIN alone does not establish import capacity.

### Consequences

- Good, because Doka and PostgreSQL work with normal migrations, and optional SQLite adapter behavior is tested without changing package prerequisites.
- Bad, because a supporting index is not a universal optimal query plan; real payload filters and workload distributions may need application indexes.
- Good, because PostgreSQL tree queries retain complete row coverage while FK checks cannot select generated tree paths as scope-only scans.
- Bad, because PostgreSQL upgrades can rebuild affected indexes, and an application-owned unfiltered index can reintroduce the measured bad plan.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests.csproj -c Release` and expect generated migration lifecycle and physical index tests to pass without SafeMigrations references.
- Run `dotnet test tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests.csproj -c Release` and require optional adapter tests to pass before claiming compatibility with the published adapter. Include SQLite script rejection, foreign database qualifier rejection, safe replay, and same-name wrong-definition drift rejection. SQL Server additionally verifies directions, stamped CHECK enforcement/trust, valid populated predicates, rejected invalid rows, complete integer-upgrade rollback, and refused narrowing.
- Run the PostgreSQL nullable-parent index tests: actual catalog predicates and public tree-query plans must match for native and converted TreeIds, while principal checks exclude generated structural paths.
- Run the million-child public import with normal FK and automatic-maintenance settings. Require completed geometry/Full validation and the unchanged additional-heap budget; observe actual RI lookup after statistics-driven replanning.
- Require positive convention-ownership and negative explicit/adopted-index tests, including custom filters, names, uniqueness, provider facets, and physical-name collisions.

## Pros and Cons of the Options

### Provider-neutral model indexes with optional adapter qualification

- Good, because ordinary EF scaffolding owns the model contract and applications can add SafeMigrations independently.
- Bad, because both ordinary and optional generated migrations need separate lifecycle and physical-catalog tests.

### Require SafeMigrations for all structural schema creation

- Good, because one migration path could centralize drift and replay policies.
- Bad, because consumers would acquire a mandatory integration dependency and lose the ordinary migration contract they requested.

### Publish manual provider-specific index SQL only

- Good, because database owners could tune access paths directly for known workloads.
- Bad, because index definitions could drift from renamed model properties, ownership metadata, and generated migration snapshots.

## More Information

The support matrix is the canonical version inventory; this record does not claim support for an unqualified future
provider release. The current optional adapter baseline is recorded in the support matrix. SQLite runtime safe migrations
and unsupported SQL-script generation are distinct contracts. SQL Server's optional 10.4.9 adapter is additive to
ordinary migration support. It admits proven built-in Int32-to-Int64 widening with exact source/storage and prior
dependency-drop proofs, and validates the four generated integer CHECKs over existing rows. Invalid coordinates
remain DataBlocked; disabled or untrusted checks remain Different. Consumer tests cover the full empty/populated
upgrade, post-upgrade Int64 capacity, each invalid CHECK's complete rollback, and refused narrowing even with fitting
values. Opaque predicates and other unproven conversions do not inherit those approvals.

### Re-evaluation Triggers

- A published SafeMigrations adapter changes recognition, prerequisite
  projection, catalog comparison, or immutable fingerprint handling for EF's
  canonical raw single-column null predicates. Requalify actual scaffolding,
  preflight, replay, catalog prerequisites, and drift against that published
  package before claiming optional adapter compatibility. A passing temporary
  prototype is feature evidence, not published-package qualification.
- SQL Server SafeMigrations changes integer-width admission, layout/dependency proofs, or CHECK stamping/trust/data proof.
  Requalify actual generated operations and complete-upgrade rollback before widening the documented adapter contract.

- A new provider version changes index metadata, key mapping, SQL generation, or physical catalog behavior.
- Query-plan evidence shows a missing structural access path or a harmful redundant index.
- PostgreSQL changes predicate implication, FK RI planning, or statistics-driven cached-plan invalidation.
- An EF convention change alters finalizer batching or nullable self-FK index coverage.
- The application requests a migration policy unavailable through the current optional adapters.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed deterministic model indexes, ordinary EF migration support, and optional SafeMigrations regression specifications against the linked repository evidence.
- 2026-10-04: Specialized untouched PostgreSQL indexes after actual atomic-import RI scans; retained application metadata and added ordinary/optional migration and query-plan regressions.
- 2026-10-06: Qualified the published SafeMigrations 10.4.8 adapters with all 61 unchanged consumer cases, including the 21 PostgreSQL failures from 10.4.5; preserved ordinary migrations and the historical adapter finding evidence.
- 2026-10-06: Added the owner-approved optional SQL Server 10.4.8 adapter consumer and dbo/explicit-schema regressions; distinguished raw CHECK proof and stamped replay from unsupported historical integer-width upgrades. The local full optional suite passed 92/92 cases (including 30 SQL Server relational cases and its tooling control), while ordinary migrations passed 49/49. SQL Server ran under x86-64 emulation on ARM; this is not native hosted qualification.
- 2026-10-08: Qualified the published SafeMigrations 10.4.9 adapters with 112/112 optional cases and 49/49 ordinary migration cases, without failures or skips. Replaced the superseded SQL Server rejection expectations with full generated Int32-to-Int64 upgrades on empty/valid populated tables, populated integer CHECK proof and stamped trusted replay, all four invalid predicates with complete upgrade rollback, and refused narrowing for fitting/oversized values. The full Release build and read-only style/import checks passed. Local SQL Server remains x86-64 emulation on ARM; native hosted qualification is separate.

### Implementation References

- [Index roles](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetIndexes.cs)
- [Model index convention](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetIndexConvention.cs)
- [PostgreSQL parent convention](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetParentIndexes.cs)
- [PostgreSQL eligibility tests](../../tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests/Indexes/NullableParentIndexTests.Eligibility.cs)
- [Index ownership tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/IndexConventionTests.PostgreSqlParents.cs)
- [Provider capabilities](../../src/Doka.EntityFrameworkCore.NestedSet/Providers)
- [Ordinary migration tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests)
- [Optional adapter tests](../../tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests)
- [SQL Server integer-upgrade regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests/Integration/Lifecycle/SafeMigrationTests.SqlServerIntegerUpgrades.cs)
- [Migration contract](../../docs/migrations.md)
- [Pinned support matrix](../../docs/support-and-qualification.md)
- [Central package versions](../../Directory.Packages.props)

### Sources

- [EF Core 10.0.12 physical index table mapping](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Metadata/Internal/RelationalModel.cs) (primary source; retrieved 2026-10-04)
- [EF Core 10.0.12 FK index prefix coverage](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Conventions/ForeignKeyIndexConvention.cs) (primary source; retrieved 2026-10-04)
- [PostgreSQL 17 multicolumn index prefixes](https://www.postgresql.org/docs/17/indexes-multicolumn.html) (primary source; retrieved 2026-10-04)

- [PostgreSQL 17 partial indexes](https://www.postgresql.org/docs/17/indexes-partial.html) (primary source; retrieved 2026-10-04)
- [PostgreSQL 17 prepared statements and plan invalidation](https://www.postgresql.org/docs/17/sql-prepare.html) (primary source; retrieved 2026-10-04)
- [PostgreSQL 17 routine maintenance and planner statistics](https://www.postgresql.org/docs/17/routine-vacuuming.html) (primary source; retrieved 2026-10-04)
- [EF Core 10.0.12 finalizing convention batches](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Conventions/Internal/ConventionDispatcher.ImmediateConventionScope.cs) (primary source; retrieved 2026-10-04)
- [SafeMigrations PostgreSQL 10.4.8 published package metadata](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.safemigrations.postgresql/10.4.8/doka.entityframeworkcore.safemigrations.postgresql.nuspec) (primary source; retrieved 2026-10-06)
- [SafeMigrations 10.4.8 canonical null-filter recognition and prerequisites](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/b9fccd20db5d72a8cb554ae162ba05c4994b5c3b/src/Doka.EntityFrameworkCore.SafeMigrations.PostgreSql/Features/Indexes/PostgreSqlSafeMigrationCatalogSqlBuilder.Filters.cs) (primary source; retrieved 2026-10-06)
- [SafeMigrations SQL Server 10.4.8 published package metadata](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.safemigrations.sqlserver/10.4.8/doka.entityframeworkcore.safemigrations.sqlserver.nuspec) (primary source; retrieved 2026-10-06)
- [SafeMigrations SQL Server 10.4.8 CHECK proof and stamps](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/b9fccd20db5d72a8cb554ae162ba05c4994b5c3b/src/Doka.EntityFrameworkCore.SafeMigrations.SqlServer/Features/Constraints/CheckConstraints/SqlServerSafeMigrationCatalogSqlBuilder.CheckConstraints.cs) (primary source; retrieved 2026-10-06)
- [SafeMigrations SQL Server 10.4.8 alteration contracts](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/b9fccd20db5d72a8cb554ae162ba05c4994b5c3b/src/Doka.EntityFrameworkCore.SafeMigrations.SqlServer/Features/Columns/SqlServerSafeMigrationCatalogSqlBuilder.Columns.cs) (primary source; retrieved 2026-10-06)
- [SafeMigrations SQL Server 10.4.8 schema resolution](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/b9fccd20db5d72a8cb554ae162ba05c4994b5c3b/docs/sqlserver-behavior.md) (primary source; retrieved 2026-10-06)
- [SafeMigrations SQL Server 10.4.9 published package metadata](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.safemigrations.sqlserver/10.4.9/doka.entityframeworkcore.safemigrations.sqlserver.nuspec) (primary source; retrieved 2026-10-08)
- [SafeMigrations 10.4.9 published release](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/releases/tag/v10.4.9) (primary source; retrieved 2026-10-08)
- [SafeMigrations SQL Server 10.4.9 integer-widening proof](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/71a2004d499658fb9bade18439bda7c0685184b0/src/Doka.EntityFrameworkCore.SafeMigrations.SqlServer/Features/Columns/SqlServerSafeMigrationCatalogSqlBuilder.IntegerWidening.cs) (primary source; retrieved 2026-10-08)
- [SafeMigrations SQL Server 10.4.9 populated integer CHECK proof and stamps](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/71a2004d499658fb9bade18439bda7c0685184b0/src/Doka.EntityFrameworkCore.SafeMigrations.SqlServer/Features/Constraints/CheckConstraints/SqlServerSafeMigrationCatalogSqlBuilder.CheckConstraints.cs) (primary source; retrieved 2026-10-08)
- [SafeMigrations SQL Server 10.4.9 schema and registration contract](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/71a2004d499658fb9bade18439bda7c0685184b0/docs/sqlserver-behavior.md) (primary source; retrieved 2026-10-08)
