# Doka.EntityFrameworkCore.NestedSet.Postgres

Native PostgreSQL integration for the shared NestedSet EF hierarchy engine.
This adapter owns native SQL, model/index conventions, registry transport, and
query translation while the common package owns hierarchy algorithms and
transaction/lifecycle coordination.

## Install

```sh
dotnet package add Doka.EntityFrameworkCore.NestedSet.Postgres --version 10.1.0
```

The adapter brings in `Doka.EntityFrameworkCore.NestedSet`, `Doka.NestedSet`,
and `Npgsql.EntityFrameworkCore.PostgreSQL` `[10.0.3,11.0.0)` transitively. Use one qualified
NestedSet version across the application's NestedSet packages. These commands
describe the `10.1.0` source contract; select the exact published release version
when installing from NuGet.

The adapter requires exactly the matching common EF kernel version, `[10.1.0]`
for this source contract. Update all selected NestedSet adapters together.

## Register

Call `UseNestedSets()` inside the native provider callback:

```csharp
using Microsoft.EntityFrameworkCore;
using Doka.EntityFrameworkCore.NestedSet;

services.AddDbContext<ApplicationDbContext>(options => options
    .UseNpgsql(connectionString, postgres => postgres.UseNestedSets()));
```

Keep entity configuration through `HasNestedSet(...)`, queries/mutations through
`context.NestedSet<TEntity>()`, and existing application save/transaction rules.
No additional hierarchy entity or provider-specific domain model is required.
Ordinary EF migrations are the baseline; SafeMigrations is independently optional.

The former options-level `DbContextOptionsBuilder.UseNestedSets()` overloads
are removed. See the [upgrade guide](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/main/docs/upgrading-provider-packages.md),
[configuration](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/main/docs/configuration.md), and
[support/qualification matrix](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/main/docs/support-and-qualification.md).
