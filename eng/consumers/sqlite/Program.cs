using System;
using System.Threading;
using Doka.EntityFrameworkCore.NestedSet;
using Doka.NestedSet.PackageConsumers;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<ConsumerContext>();
options.UseSqlite("Data Source=:memory:", sqlite => sqlite.UseNestedSets());
await ConsumerContext.VerifyQueryAsync(options.Options);

await using var context = new ConsumerContext(options.Options);
await context.Database.OpenConnectionAsync(CancellationToken.None);
await context.Database.EnsureCreatedAsync(CancellationToken.None);

var treeId = Guid.NewGuid();
await context.NestedSet<Node>().InsertRootAsync(new Node { Name = "Root" }, treeId, CancellationToken.None);

var saved = await context.NestedSet<Node>().InTree(treeId).Nodes.SingleAsync(CancellationToken.None);

if (saved.Name != "Root" || saved.Right - saved.Left != 1 || saved.Depth != 0 || saved.Position != 0)
{
    throw new InvalidOperationException("Candidate SQLite hierarchy insertion/read failed.");
}
