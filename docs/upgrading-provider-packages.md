# Upgrade NestedSet packages

## Upgrade from 10.2.0 to 10.3.0

`10.3.0` keeps .NET 10 and EF Core 10. Update all selected NestedSet adapters
and the common EF package together; each adapter requires kernel `[10.3.0]`.
Owned asynchronous operations require an explicit `CancellationToken`. Source
calls that previously omitted the argument must pass the operation's token.
For entry points with no cancellation source, use `CancellationToken.None`
deliberately:

```csharp
await hierarchy.InsertRootAsync(root, treeId, cancellationToken);
await hierarchy.InTree(treeId).PlanRebuildAsync(CancellationToken.None);
```

The parameter still accepts None; the change requires an explicit choice rather
than a cancelable token. Tokens keep their identity through execution, and
pre-cancellation, rollback and retry guarantees are unchanged. The binary method
signatures remain compatible with already compiled callers. Required-token
signatures are recorded in the `10.3.0` shipped baseline; the historical optional
parameters remain in the [10.2.0 baseline](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/v10.2.0/src/Doka.EntityFrameworkCore.NestedSet/PublicAPI.Shipped.txt).

The source-breaking change uses the
[documented version exception](release-governance.md#version-policy).
Provider registration, mappings and schema are unchanged; no database migration
is required. Mandatory rollback, disposal and terminal command finalization
stay uninterruptible.

Actual EF/.NET overrides and interface implementations preserve the upstream
optional parameter contract. Calls made through those base types and interfaces
therefore remain optional; this exception does not apply to owned methods.

## Upgrade from 10.1.0 to 10.2.0

`10.2.0` is an additive update on .NET/EF Core 10. Update all selected NestedSet
adapters and the common EF package together; each adapter requires kernel
`[10.2.0]`. The provider-callback registration remains unchanged.

Existing mappings retain their generated registry name and require no migration
for this update. To adopt `HasTreeRegistryTable(...)`, scaffold and review the
[data-preserving registry rename](migrations.md#explicit-registry-table-name).
Regenerate compiled models after changing that model configuration.

With `AutoDetectChangesEnabled = false`, unmarked application payload remains
unrecorded across ordinary, coordinated and facade saves. Mark intended changes
through EF entry APIs, notifications or an application detection pass. Unrecorded
hierarchy edits still reject rather than triggering context-wide detection.
Review [manual detection and recovery](configuration.md#manual-change-detection)
if application code relied on the previously unintended detection. Callback
writes remain subject to the existing explicit-write and hierarchy guards.

The update also strengthens recovery of relationships, complex containers and
table-sharing peers, and prepared-owner imports across batches. These fixes
retain the documented transaction, cancellation, retry and restoration contracts.
No SafeMigrations dependency or new database/provider requirement is introduced.

## Registration transition introduced in 10.1.0

The stable `10.1.0` contract changes package installation and infrastructure
registration. `Doka.EntityFrameworkCore.NestedSet` remains the common EF kernel;
its hierarchy facade, entity mapping, structural algorithms, and transaction
contracts remain shared. `Doka.NestedSet` remains the small ORM-independent
package. The native integrations are separate shipping packages.

This is a breaking registration change from `10.0.0`, not an additive overload.
The former `DbContextOptionsBuilder.UseNestedSets()` overloads are removed.
Register the selected adapter inside the actual database provider's callback.
The decision and trade-offs are recorded in
[D-020](decisions/D-020-provider-owned-runtime-packages.md). The owner explicitly
kept `10.1.0` for this break as a one-time SemVer exception: the major follows
the .NET family and `11.x` is reserved for .NET 11. This does not authorize
other breaking minor updates. Review/pin the application's qualified package
graph when crossing this boundary; a floating 10.x version range does not
preserve the removed registration API.

## Select the application's adapter

Install the adapter matching each context's database. It brings the common EF
kernel, `Doka.NestedSet`, and the matching EF database provider transitively.
Use one qualified NestedSet version for all NestedSet packages in an application.
The commands below install the stable `10.3.0` source contract. Applications
coming from `10.0.0` must also apply the registration transition described here.

Each adapter depends on exactly that release of the common EF kernel:
`[10.3.0]` for this release. The adapter implements assembly-internal kernel
contracts, so a different kernel patch is not an advertised compatible
combination. Update all selected adapters together, including applications
using different providers in separate contexts. The public-only dependency
from the kernel to `Doka.NestedSet` remains a minimum version.

NuGet can let a direct application reference override a dependency constraint
and report [NU1608](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1608).
Do not suppress that warning to mix NestedSet adapter/kernel releases. A
warnings-as-errors restore rejects it; an ordinary restore warning alone is
not an unconditional runtime barrier.

| Database | Package to install | Native dependency range |
| --- | --- | --- |
| MySQL or MariaDB | `Doka.EntityFrameworkCore.NestedSet.MySql` | Doka `[10.6.4,10.7.0)` |
| PostgreSQL | `Doka.EntityFrameworkCore.NestedSet.Postgres` | Npgsql `[10.0.3,11.0.0)` |
| SQLite | `Doka.EntityFrameworkCore.NestedSet.Sqlite` | Microsoft SQLite `[10.0.13,11.0.0)` |
| SQL Server | `Doka.EntityFrameworkCore.NestedSet.SqlServer` | Microsoft SQL Server `[10.0.13,11.0.0)` |

For example:

```sh
dotnet package add Doka.EntityFrameworkCore.NestedSet.MySql --version 10.3.0
```

A provider already referenced directly by the application must satisfy the
adapter's range. The common kernel declares EF/Relational
`[10.0.12,11.0.0)`. Doka 10.6.4 also requires EF Relational below `10.1.0`, so
the effective MySQL/MariaDB intersection is `[10.0.12,10.1.0)`. The SQLite and
SQL Server adapters require their native provider from `10.0.13`; they do not
extend the common kernel's supported EF major. Restore and review the resulting
application graph rather than treating these separate ranges as interchangeable.

`Postgres` is the NestedSet package suffix. Npgsql and optional SafeMigrations
retain their own published package names. MySQL and MariaDB use the same
NestedSet `.MySql` adapter and the Doka provider. Pomelo is outside the contract.

## Move registration into the provider callback

Keep `using Doka.EntityFrameworkCore.NestedSet;`. The adapter-specific extension
methods use that common namespace, with distinct native builder receivers.
Each method returns the same native builder, so other provider options remain
composable in the callback.

Doka MySQL/MariaDB:

```csharp
var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));

services.AddDbContext<ApplicationDbContext>(options => options
    .UseMySql(connectionString, serverVersion, mysql => mysql.UseNestedSets()));
```

PostgreSQL:

```csharp
services.AddDbContext<ApplicationDbContext>(options => options
    .UseNpgsql(connectionString, postgres => postgres.UseNestedSets()));
```

SQLite:

```csharp
services.AddDbContext<ApplicationDbContext>(options => options
    .UseSqlite(connectionString, sqlite => sqlite.UseNestedSets()));
```

SQL Server:

```csharp
services.AddDbContext<ApplicationDbContext>(options => options
    .UseSqlServer(connectionString, sqlServer => sqlServer.UseNestedSets()));
```

Merge this call into an existing provider callback rather than discarding its
`MigrationsAssembly`, retry, collection, or other native provider configuration:

```csharp
options.UseMySql(connectionString, serverVersion, mysql =>
{
    mysql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
    mysql.UseNestedSets();
});
```

Calling the same adapter registration repeatedly is idempotent. Different
adapters cannot be selected for one context options graph. The common options
extension validates the configured provider's exact identity before hierarchy
I/O; compiled models do not bypass adapter selection. Installing several
adapter packages for different context types does not activate them globally.
Each context selects its own adapter through its native callback, without
assembly scanning or a process-wide provider registry.

## Keep entity and save configuration

`HasNestedSet(...)`, `IEntityTypeConfiguration<TEntity>`, `TreeId`, optional
Scope, bounds, `Depth`, sibling ordering, shadow properties, and public query
and mutation methods stay in the common EF package. No additional domain
entity or provider-specific hierarchy model is required.

`ModelBuilder.UseNestedSets()` and
`ModelConfigurationBuilder.UseNestedSets()` remain model-building APIs. They
are not a substitute for selecting the runtime adapter. The normal callback
registration installs the shared model conventions; applications do not need
to add the former model-level infrastructure calls as well.

Keep `SaveNestedSetChangesAsync` or the optional `NestedSetDbContext` base for
automatic Parent/order maintenance, and keep application transaction and retry
ownership unchanged. Structural insertion still uses the explicit facade.

The split changes implementation ownership, not the stored hierarchy format.
Review the generated model/migration diff on upgrade, preserve existing
structural indexes and registry identities, and regenerate supplied compiled
models with the current adapter/conventions. Do not rebuild bounds or rewrite
tree identities merely because a package changed.

## Keep migration adapters optional

Ordinary EF migrations remain the baseline for every provider. A NestedSet
adapter does not install SafeMigrations. If the application uses SafeMigrations,
keep its corresponding package and registration independently:

```csharp
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(connectionString, sqlServer => sqlServer.UseNestedSets())
    .UseSqlServerSafeMigrations();
```

See [migrations](migrations.md) and the
[qualification matrix](support-and-qualification.md) for actual provider,
engine, optional adapter, and native-platform evidence. Historical qualification
results identify their own package layout and source; they do not establish a
fresh qualification of this split.
