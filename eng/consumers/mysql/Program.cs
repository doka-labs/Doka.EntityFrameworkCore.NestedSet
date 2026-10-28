using System;
using Doka.EntityFrameworkCore.MySql;
using Doka.EntityFrameworkCore.NestedSet;
using Doka.NestedSet.PackageConsumers;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<ConsumerContext>();
options.UseMySql(
    "Server=127.0.0.1;Database=candidate;User ID=unused;Pooling=false",
    MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
    mysql => mysql.UseNestedSets());

await ConsumerContext.VerifyQueryAsync(options.Options);
