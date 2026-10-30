using Doka.EntityFrameworkCore.NestedSet;
using Doka.NestedSet.PackageConsumers;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<ConsumerContext>();
options.UseSqlServer(
    "Server=127.0.0.1;Database=candidate;User ID=unused;Password=unused;Encrypt=false;Pooling=false",
    sqlServer => sqlServer.UseNestedSets());

await ConsumerContext.VerifyQueryAsync(options.Options);
