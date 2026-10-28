# Upgrade to provider-owned packages

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
The commands below upgrade an application to stable `10.1.0`.

Each adapter depends on exactly that release of the common EF kernel:
`[10.1.0]` for this release. The adapter implements assembly-internal kernel
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
| MySQL or MariaDB | `Doka.EntityFrameworkCore.NestedSet.MySql` | Doka `[10.6.2,10.7.0)` |
| PostgreSQL | `Doka.EntityFrameworkCore.NestedSet.Postgres` | Npgsql `[10.0.3,11.0.0)` |
| SQLite | `Doka.EntityFrameworkCore.NestedSet.Sqlite` | Microsoft SQLite `[10.0.13,11.0.0)` |
| SQL Server | `Doka.EntityFrameworkCore.NestedSet.SqlServer` | Microsoft SQL Server `[10.0.13,11.0.0)` |

For example:

```sh
dotnet package add Doka.EntityFrameworkCore.NestedSet.MySql --version 10.1.0
```

A provider already referenced directly by the application must satisfy the
adapter's range. The common kernel declares EF/Relational
`[10.0.12,11.0.0)`. Doka 10.6.2 also requires EF Relational below `10.1.0`, so
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
