using Doka.EntityFrameworkCore.NestedSet;
using Doka.NestedSet.PackageConsumers;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<ConsumerContext>();
options.UseNpgsql(
    "Host=127.0.0.1;Database=candidate;Username=unused;Pooling=false",
    postgres => postgres.UseNestedSets());

await ConsumerContext.VerifyQueryAsync(options.Options);
