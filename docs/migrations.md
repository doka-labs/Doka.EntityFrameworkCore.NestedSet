# Migrations and indexes

NestedSet contributes ordinary EF Core model metadata. Normal EF migrations are
the required schema path. SafeMigrations is an optional additional policy and
is never a runtime prerequisite.

## Generated model

After adding `.UseNestedSets()` to context options and
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
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | No adapter required by this repository |

Doka, Npgsql, SQLite, and SQL Server work through ordinary EF migrations without
SafeMigrations. Optional integration tests verify that SafeMigrations 10.4.5
accepts the same finalized model for MySQL, MariaDB, PostgreSQL, and SQLite.
Pomelo is outside the supported provider contract.

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
