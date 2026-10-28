# Doka.EntityFrameworkCore.NestedSet.SqlServer

Native SQL Server integration for the shared NestedSet EF hierarchy engine.
This adapter owns native SQL, model/index conventions, registry transport, and
query translation while the common package owns hierarchy algorithms and
transaction/lifecycle coordination.

## Install

```sh
dotnet package add Doka.EntityFrameworkCore.NestedSet.SqlServer --version 10.1.0
```

The adapter brings in `Doka.EntityFrameworkCore.NestedSet`, `Doka.NestedSet`,
and `Microsoft.EntityFrameworkCore.SqlServer` `[10.0.13,11.0.0)` transitively. Use one qualified
NestedSet version across the application's NestedSet packages. Version `10.1.0`
is the stable six-package contract.

The adapter requires exactly the matching common EF kernel version, `[10.1.0]`
for this release. Update all selected NestedSet adapters together.

The shipping dependency graph explicitly raises `System.Security.Cryptography.Pkcs`
to a minimum of `9.0.21`, without an upper bound, for the native SQL Client path.
That security floor is a SQL Server adapter dependency, not a dependency of
the common kernel. Repository qualification uses PKCS 10.0.14 for .NET 10;
applications already using the patched 9.0.21 need no forced update.
Native SQL Server qualification requires supported Linux x86-64 hosts; ARM emulation
is not the supported qualification environment.

## Register

Call `UseNestedSets()` inside the native provider callback:

```csharp
using Microsoft.EntityFrameworkCore;
using Doka.EntityFrameworkCore.NestedSet;

services.AddDbContext<ApplicationDbContext>(options => options
    .UseSqlServer(connectionString, sqlServer => sqlServer.UseNestedSets()));
```

Keep entity configuration through `HasNestedSet(...)`, queries/mutations through
`context.NestedSet<TEntity>()`, and existing application save/transaction rules.
No additional hierarchy entity or provider-specific domain model is required.
Ordinary EF migrations are the baseline; SafeMigrations is independently optional.

The former options-level `DbContextOptionsBuilder.UseNestedSets()` overloads
are removed. See the [upgrade guide](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/main/docs/upgrading-provider-packages.md),
[configuration](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/main/docs/configuration.md), and
[support/qualification matrix](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/main/docs/support-and-qualification.md).
