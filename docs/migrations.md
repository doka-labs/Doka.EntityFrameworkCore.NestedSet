# Migrations and indexes

NestedSet contributes ordinary EF Core model metadata. Normal EF migrations are
the required schema path. SafeMigrations is an optional additional policy and
is never a runtime prerequisite.

## Generated model

After selecting the NestedSet adapter inside the native provider callback and
`.HasNestedSet(...)` to an entity configuration, scaffold and review a
migration:

```sh
dotnet ef migrations add AddFolderHierarchy --project src/App --startup-project src/App
```

For Doka MySQL/MariaDB, PostgreSQL, and SQL Server, an idempotent script can
cover databases at different migration versions:

```sh
dotnet ef migrations script --idempotent --project src/App --startup-project src/App
```

SQLite cannot generate idempotent migration scripts. If the current migration
is known, generate a script starting there; otherwise apply migrations with
`dotnet ef database update`:

```sh
dotnet ef migrations script CurrentMigration --project src/App --startup-project src/App
dotnet ef database update --project src/App --startup-project src/App
```

Choose one SQLite path for the deployment. See the EF Core
[SQLite migration limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations#idempotent-script-limitations).

The final model contains:

- the mapped structural columns;
- portable checks for positive bounds, depth, and position where the table
  shape supports them;
- the restrictive self-FK where EF can represent it;
- automatically derived structural indexes; and
- one typed tree-registry table per configured hierarchy.

`EnsureCreated` is suitable only for disposable tests and examples. It does not
replace migration history for an evolving application database.

## Typed tree registry

Each hierarchy receives a deterministic table named
`DokaNestedSetTrees_<hash>` in the hierarchy table's schema. The hash uses the
physical schema, hierarchy table, node-key column, optional scope column, and
TreeId column. Moving or renaming a CLR entity without changing those physical
identifiers therefore keeps the same registry table. Different physical
hierarchy mappings remain separate, and the name stays below provider
identifier limits.

Regenerate any application-owned EF compiled model after changing the NestedSet
version or hierarchy mapping, before deploying a migration based on the new
model. A compiled model captures the old registry name until regenerated. When
an application uses EF's build-time generation, verify that generation has run
for the new model. See EF Core's [compiled-model guidance][compiled-models] and
[MSBuild integration](https://learn.microsoft.com/en-us/ef/core/cli/msbuild).

[compiled-models]: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-models

If an existing application migration or database used the earlier
CLR-name-based registry hash, the physical registry name changes once when the
application updates NestedSet. Review the scaffolded migration before applying
it. Replace any generated registry `DropTable`/`CreateTable` pair with a
`RenameTable` operation from the actual old name to the actual new name in the
same schema; reverse that rename in `Down`. Keep the primary key, active rows,
revisions, and tombstones in place. Do not recreate or reseed the registry:
doing so can silently make a retired TreeId reusable. Derive both names from
the application's previous model snapshot and new generated model, then test
the migration on a database containing an active tree and a tombstone. EF Core
documents why automatically scaffolded renames require this manual review in
[Managing Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing).
Use [MigrationBuilder.RenameTable](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.migrations.migrationbuilder.renametable?view=efcore-10.0)
with the actual old and new registry names, for example:

```csharp
migrationBuilder.RenameTable(
    name: oldRegistryTableName,
    schema: hierarchySchema,
    newName: newRegistryTableName);
```

The registry key is `TreeId` for an unscoped hierarchy and `(Scope, TreeId)` for
a scoped hierarchy. Identity properties copy the node mapping's CLR and
provider types, converter, comparer, length, Unicode, precision, scale, fixed
length, column type, and collation. `Revision` records structural progress and
`Lifecycle` distinguishes active identities from tombstones.

Applications do not seed, modify, or delete registry rows. Mutations create and
lock active rows, and deletion or merge preserves a tombstone so a historical
TreeId cannot be reused accidentally.

## Generated structural indexes

The final convention derives exact ordinary B-tree paths from completed model
metadata.

For a scoped hierarchy:

```text
(Scope ASC, TreeId ASC, Left ASC)
(Scope ASC, TreeId ASC, Right ASC)
(Scope ASC, TreeId ASC, ParentId ASC, Position ASC)
```

For an unscoped hierarchy, only Scope is omitted:

```text
(TreeId ASC, Left ASC)
(TreeId ASC, Right ASC)
(TreeId ASC, ParentId ASC, Position ASC)
```

Configured sibling ordering adds one path. For:

```csharp
.OrderBy(folder => folder.Name)
.ThenByDescending(folder => folder.Type)
```

the scoped index is:

```text
(Scope ASC, TreeId ASC, ParentId ASC, Name ASC, Type DESC, NodeKey ASC)
```

NodeKey is appended exactly once as the deterministic ascending tie breaker.
The convention reuses an existing non-unique ordinary B-tree index only when
its complete property sequence and direction vector match. Compatible provider
facets such as INCLUDE columns may coexist. Filtered, partial, expression,
prefix, or specialized-method indexes are not assumed equivalent.

Convention-owned names use a deterministic hash of final entity, table,
schema, column, and direction identity. An application-customized index remains
application-owned and is not removed when the hierarchy configuration changes.

### PostgreSQL index predicates

PostgreSQL keeps the same structural key sequences, but adds
`"TreeId" IS NOT NULL` to untouched library-created structural indexes. TreeId
is required, so every hierarchy row remains indexed. Tree-local equality
queries imply this predicate, including parameterized queries with converted
TreeIds. A self-FK principal check constrains only `(Scope?, NodeKey)` and does
not imply the TreeId predicate.

This distinction prevents a principal check from choosing a scope-only scan
of a structural index during a large atomic import. In qualification, automatic
statistics collection saw no committed rows while hundreds of thousands of
new rows were visible to the importing transaction. A replanned FK lookup
then scanned the growing scope for every child. A nullable-Parent predicate
on the dependent index alone was insufficient: another structural index
remained eligible. The TreeId predicates exclude those generated paths from
that principal lookup without disabling automatic maintenance or changing
the FK. See PostgreSQL's [partial-index rules][pg-partial] and
[statistics-driven prepared-plan invalidation][pg-prepare].

An untouched convention-created nullable self-FK index is also partitioned:

```text
(Scope?, ParentId) WHERE ParentId IS NOT NULL
(Scope?, NodeKey, ParentId) WHERE ParentId IS NULL
```

The first path indexes dependents; the second retains a root access path.
The trailing Parent column keeps the root index on tables that actually map
the filter column. EF's TPT table mapping uses index keys rather than filter
references; a key-only root index could otherwise reach a payload table without
Parent. The leading Scope/NodeKey prefix remains available, and Parent is null
on every indexed row.
The root index name derives from physical schema, table, Parent, and principal
columns, rather than the CLR entity name. These predicates use the actual
mapped and delimited column names. PK and alternate-key uniqueness, FK
definition, and the required structural key sequences remain unchanged.

Explicit or adopted application indexes are preserved, including names,
filters, uniqueness, directions, and provider facets. Other providers retain
their existing index definitions. An application-owned unfiltered scope-leading
index can still make the expensive FK plan eligible; inspect the application's
actual plans after adding such an index. No model convention can promise an
optimal plan for arbitrary application indexes or statistics.

When upgrading from a model with unfiltered PostgreSQL indexes, scaffold a
normal migration. Review its index drop/create operations and filters against
the model snapshot; hierarchy rows and registry lifecycle data must remain
intact. Account for index-build locks and extra disk space in the application's
deployment policy. The optional SafeMigrations regression cases require replay
and drift support for both Parent predicates and the TreeId predicates. Their
creation, preflight, replay and drift outcomes belong to the updated adapter
contract. Repository qualification now declares published Doka 10.6.2 and
SafeMigrations 10.6.0. No legacy test contract or compatibility skip is retained.
See the [provider qualification](qualification/provider-migrations.md) for the
declared public graph, its executed 2026-10-25 local scope and the separate dated
local evidence. SafeMigrations remains optional.

[pg-partial]: https://www.postgresql.org/docs/17/indexes-partial.html
[pg-prepare]: https://www.postgresql.org/docs/17/sql-prepare.html

## Optional ancestor index

`HasAncestorIndex()` adds a generated required Boolean, named
`NestedSetIsNonLeaf` by default, and one non-unique ascending index:

```text
(Scope?, NestedSetIsNonLeaf, TreeId, Right, Left, NodeKey)
```

The expression is derived from the final physical `Right` and `Left` columns.
It contains no application visibility, metadata, or payload field. Scope is
omitted when absent, repeated property identities are removed, and the existing
three structural indexes and optional ordering index remain. The ordinary
index ownership, exact adoption, naming, and collision rules apply to this
additional path as well.

For published Doka 10.5.0 models with implicit `utf8mb4` lengths on Scope, TreeId, and
NodeKey together, native qualification reproduced error 1071 on both MySQL and
MariaDB: three automatically assigned 255-character keys consume 3,060 bytes;
the Boolean and two Int64 bounds bring this index to 3,077 bytes, above the
3,072-byte InnoDB limit. Doka's automatic length assignment becomes explicit
metadata before the complete later index can be rebudgeted. NestedSet's early
index finalizer does not correct this independent provider defect, and the
library does not silently shorten identities. Explicit application-domain
lengths have their own qualification; that older published cohort did not
qualify the affected implicit multi-string model.

The [corrected private provider acceptance](qualification/query-composition.md#optional-ancestor-access)
created the complete implicit-string schema on both engines without index
prefixes. Its lengths remain convention-owned and parent-key compatible.
That dated local result is separate from fresh qualification of the currently
declared published Doka 10.6.2; review new column-length operations when adopting
the provider correction.

| Provider | Generated value | Storage |
| --- | --- | --- |
| Doka MySQL/MariaDB | Delimited guarded nonleaf predicate below | Virtual |
| PostgreSQL 17 | Delimited guarded nonleaf predicate below | Stored |
| SQLite | Delimited guarded nonleaf predicate below | Virtual |
| SQL Server | `CONVERT(bit, CASE WHEN predicate THEN 1 ELSE 0 END)`, using the guarded predicate below | Non-persisted |

In role notation, before substituting provider-delimited mapped column names,
the guarded predicate is:

```sql
Right > CASE
    WHEN Left = 9223372036854775807 THEN Left
    ELSE Left + 1
END
```

A split table can represent zero-width or reversed intervals without a
cross-fragment width CHECK. Guarding the addition inside scalar CASE preserves
classification of every signed Int64 pair, including Left at its maximum,
without depending on the optimizer's evaluation order for Boolean AND. Normal
invariant checks, validation, and repair remain separate contracts.

These choices use each provider's computed-column support. PostgreSQL 17
requires stored generated columns. SQLite permits adding a virtual generated
column to a populated table but does not permit adding a stored one with
`ALTER TABLE ADD COLUMN`. MySQL and MariaDB can index virtual generated columns;
SQL Server can index an eligible deterministic non-persisted expression. See
[PostgreSQL generated columns][pg-generated], [SQLite generated columns][sqlite-generated],
[MySQL generated indexes][mysql-generated], [MariaDB generated columns][mariadb-generated],
and [SQL Server computed indexes][sqlserver-generated].

SQL Server's indexed-computed-column connection contract also applies to
deployment and subsequent writers: `ANSI_NULLS`, `ANSI_PADDING`, `ANSI_WARNINGS`,
`ARITHABORT`, `CONCAT_NULL_YIELDS_NULL`, and `QUOTED_IDENTIFIER` must be on, while
`NUMERIC_ROUNDABORT` must be off. A read connection with incompatible settings
can make the optimizer ignore the index. NestedSet does not silently change
application session settings; qualify the application's actual connections
alongside its migration runner.

Enabling, renaming, or removing the option changes the EF model. Scaffold and
review a normal migration, including the generated expression, physical table,
index column order, upgrade data, and `Down` operations. Apply it before the
new application model is deployed. Regenerate any compiled model after the
configuration changes. Existing models that omit the option retain their
current schema and query strategy; the library does not probe the database
catalog at query time to guess whether an index has been installed.

The [2026-10-19 qualification](qualification/query-composition.md#optional-ancestor-access)
records generated-model writes and queries independently for released Doka,
source binding only, and both prepared public APIs. These query-capability
checks do not relabel earlier complete migration/lifecycle cohorts or establish
native SQL Server execution on ARM. Storage and write-maintenance observations
also remain separate from migration correctness.

An existing compatible property can be adopted; conflicting application
metadata is rejected. Reconfiguration removes only unused convention-owned
properties and untouched library-owned indexes. It does not drop an
application-created property or customized index to force an optimization.

Ordinary EF migrations remain the independent schema path. Enabling an
optional SafeMigrations adapter adds its own generated-column and index
approval, replay, and drift contract; it does not make this option or ordinary
hierarchy operations depend on SafeMigrations. The generated-column limitations
below must be considered separately from index-only compatibility.

<a id="optional-safemigrations-limits-for-the-ancestor-flag"></a>

### Optional SafeMigrations generated-column contracts

The current SafeMigrations contract covers the canonical generated nonleaf
expression and the four generated integer CHECK predicates. The tests require
actual fresh creation and populated upgrades,
physical generated-column facets and values, exact migration history, and
direct safe-operation replay. A second `MigrateAsync` call alone would stop at
migration history and would not exercise the guards.

SQLite safe-owned table creation declares the generated column's `INTEGER`
type and `NOT NULL` facet. Ordinary EF-created SQLite tables retain their own
DDL facets. A missing declared type or required facet in such an existing
table must still be rejected; accepting the expression is not permission to
repair externally different schema. Index drift and invalid existing
coordinate rows remain independent rejection contracts.

Use the declared published Doka 10.6.2 and SafeMigrations Core/adapters 10.6.0
graph. Do not mix a new Core with older adapters or infer published-package
qualification from an earlier local run. See
[provider qualification](qualification/provider-migrations.md) for the executed
2026-10-25 public scope and exact dated local inputs, commands, and results.
Neither shipping NestedSet package
requires SafeMigrations.

#### Historical published 10.5.0 boundary

The published 10.5.0 adapters did not support every generated-column path used
by the option. Ordinary EF migrations remain the supported independent route
for creating and upgrading this schema.

| Adapter 10.5.0 | Fresh generated hierarchy table | Populated generated-column upgrade |
| --- | --- | --- |
| MySQL/MariaDB | Fails closed with `opaque_sql_expression` | Fails closed with `opaque_sql_expression`; data and migration history remain unchanged |
| PostgreSQL | Fails closed with `opaque_sql_expression` | Fails closed with `opaque_sql_expression`; data and migration history remain unchanged |
| SQLite | Creation is accepted, but its own table postcondition rejects the catalog facets and the batch rolls back | Generated `ADD COLUMN` path succeeds and preserves rows |
| SQL Server | Native adapter qualification requires the x64 test lane | Native adapter qualification requires the x64 test lane |

The MySQL/MariaDB and PostgreSQL cases are an explicit adapter policy boundary:
the scaffolded `ComputedColumnSql` is classified as opaque SQL rather than a
structured computed expression. This is not evidence of a Doka DDL defect and
is not permission to bypass adapter SQL checks. Supporting automatic generated
columns requires a separately qualified structured-expression route or narrow
canonical recognizer in SafeMigrations.

The SQLite fresh-table case is a distinct reproduced adapter inconsistency.
EF's generated table DDL declares the computed column without a store type or
`NOT NULL`. `pragma_table_xinfo` consequently reports an empty declared type
and `notnull = 0`, while the model specifies required `INTEGER`. The adapter
compares those facets and rejects its own newly created table with
`doka_sm_postcondition`. The transaction removes the new table and records no
migration. The adapter does read generated columns through `table_xinfo`; this
is not a missing-column discovery issue. Its standalone generated-column ADD
path emits explicit type/nullability facets, explaining the different upgrade
outcome.

Index-only replay and wrong-definition drift checks are independently covered
on an ordinary EF-created generated-column schema. A passing safe index
operation does not establish support for fresh safe table creation or generated
column upgrades. See the
[explicit adapter regression cases](../tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests/Integration/Indexes/AncestorIndexSafeMigrationTests.cs).
No SafeMigrations package is required by either shipping NestedSet package.

[pg-generated]: https://www.postgresql.org/docs/17/ddl-generated-columns.html
[sqlite-generated]: https://www.sqlite.org/gencol.html
[mysql-generated]: https://dev.mysql.com/doc/refman/8.4/en/generated-column-index-optimizations.html
[mariadb-generated]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/generated-columns
[sqlserver-generated]: https://learn.microsoft.com/en-us/sql/relational-databases/indexes/indexes-on-computed-columns

## Existing tables

Adopting NestedSet on populated data requires an explicit transition:

1. add nullable or temporary structural columns;
2. assign Scope and TreeId from authoritative application data;
3. validate the Parent graph for missing parents, cycles, and ambiguous roots;
4. create registry rows idempotently for every exact tree identity;
5. derive Position, Depth, Left, and Right in bounded batches;
6. validate every exact tree;
7. add final checks, self-FK, and derived indexes;
8. make required columns non-null; and
9. enable application writes through the hierarchy facade.

Do not invent TreeIds from row order or bounds. A damaged or ambiguous Parent
graph needs an application decision before the library can reconstruct derived
coordinates.

## Provider matrix

| Database | EF Core provider | SafeMigrations |
| --- | --- | --- |
| MySQL/MariaDB | `Doka.EntityFrameworkCore.MySql` | Optional MySQL adapter |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | Optional PostgreSQL adapter |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | Optional SQLite adapter |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | Optional SQL Server adapter |

Doka, Npgsql, SQLite and SQL Server work through ordinary EF migrations without
SafeMigrations. The [support matrix](support-and-qualification.md#migration-matrix)
owns repository pins and the separate published/local qualification boundaries.
Neither shipping package depends on an adapter.

PostgreSQL qualification includes canonical provider-delimited single-column
`IS NULL` and `IS NOT NULL` index predicates, catalog prerequisites, replay and
same-name wrong-definition drift rejection. Recognition preserves authored SQL
and immutable index contracts; it does not permit arbitrary raw index SQL.
These forms were rejected by older adapters as `opaque_sql_expression`; the
accepted contract requires actual migration/schema/data/history/replay evidence,
not a history-only second migration. See the
[provider/migration summary](qualification/provider-migrations.md).
Pomelo is outside the supported provider contract.

## Optional SQL Server SafeMigrations

Install the adapter explicitly in the application or migration project:

```sh
dotnet add package Doka.EntityFrameworkCore.SafeMigrations.SqlServer --version 10.6.0
```

Keep a direct `Microsoft.EntityFrameworkCore.Design` reference in the project
used by EF tooling. The adapter's build assets register its provider-specific
design-time services. Enable the ordinary provider and both extensions:

```csharp
using Doka.EntityFrameworkCore.SafeMigrations.SqlServer;

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(connectionString, sqlServer => sqlServer.UseNestedSets())
    .UseSqlServerSafeMigrations();
```

NestedSet does not change its entity configuration when the adapter is enabled.
Generated structural indexes retain exact column order, directions, schema,
and replay/drift behavior.

Unqualified SQL Server safe operations require the database user's default
schema to be `dbo`. If that default differs, map the target schema explicitly
in the EF model, for example with `modelBuilder.HasDefaultSchema("hierarchy")`
or `ToTable("Folders", "hierarchy")`. The adapter rejects ambiguous unqualified
operations before mutation rather than assuming they target `dbo`.

The SQL Server adapter can validate NestedSet's four generated integer CHECK
predicates over existing rows. Valid data produces `Missing`/`Apply`; a
violated predicate produces `DataBlocked` and runtime `doka_sm_data_blocked`.
Creation establishes a stamped, enabled, trusted constraint, which subsequently
replays as `Matching`/`NoOp`. Disabling it or reenabling it without establishing
trust still produces `Different` and is not silently repaired. Opaque CHECKs
outside the supported integer-predicate grammar retain the empty-table boundary.

The adapter's `RepairIfSafe` policy now approves proven built-in `int`-to-`bigint`
coordinate widening. The actual scaffolded historical upgrade works on empty
tables and valid populated hierarchies, preserving rows while establishing
the target indexes, constraints, and Int64 capacity. Exact source metadata and
dependency ordering remain required: drop dependent indexes or constraints
before ALTER, then recreate the desired target contract. Changed default/identity
semantics or unsupported physical layouts are not implicitly approved.

Invalid existing coordinates make the new CHECK fail, and EF's migration
transaction rolls back preceding column and index changes along with migration
history. This is rejection, not automatic data repair. Reverse `bigint`-to-`int`
narrowing remains refused even when every existing value fits Int32. Ordinary
EF migrations remain independently supported without enabling SafeMigrations.

See the [10.6.0 SQL Server registration and behavior](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/e81b850480dcbff4d479e71edf60d3651b8ddf8d/docs/sqlserver-behavior.md)
and the [consumer regression coverage](regression-coverage.md#optional-sql-server-migrations).

## MySQL and MariaDB runtime rejection diagnostics

SafeMigrations 10.6.0 represents positively attributed MySQL/MariaDB runtime
guards with `SafeMigrationExecutionException`. Consumers can inspect its
`GuardCode`, for example `doka_sm_different` or `doka_sm_unsupported`,
and retain the original native database exception through `InnerException`.
Ordinary database failures are not automatically converted into owned guard
rejections. The NestedSet integration suite checks the typed wrapper, exact
guard identity, and preserved native failure together with unchanged catalogs,
hierarchy rows, and migration history. PostgreSQL and SQL Server retain their
existing native rejection assertions; SQLite retains its own runtime contract.

See the [10.6.0 diagnostic exception](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/e81b850480dcbff4d479e71edf60d3651b8ddf8d/src/Doka.EntityFrameworkCore.SafeMigrations/Diagnostics/SafeMigrationExecutionException.cs).

## Deployment verification

Before routing writers to a migrated database:

1. inspect generated SQL for the exact provider and server line;
2. apply the migration to both an empty database and a representative upgrade
   fixture;
3. inspect registry identity types, checks, self-FK, index columns, and index
   directions in the physical catalog;
4. run insert, move, detach, delete, ordered save, validation, and rebuild
   scenarios;
5. prove that equal bounds in distinct TreeIds and Scopes do not mix;
6. prove rollback with hierarchy and domain rows in one caller transaction; and
7. retain the migration, catalog evidence, and test output with the release
   candidate.

Repository integration tests exercise the pinned provider matrix. They cannot
prove compatibility with an application's extra triggers, permissions,
collations, indexes, or external writers.

## Rollback

Structural columns and registry lifecycle become application data after the
feature is enabled. A down migration that drops them destroys hierarchy state.
Prefer a forward repair, a restored backup, or an application rollback that
keeps the schema readable. When provider or EF versions change, scaffold the
same model from a clean snapshot and compare migration operations, SQL,
physical catalog, and mutation tests.
