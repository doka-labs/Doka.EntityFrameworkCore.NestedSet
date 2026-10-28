using System;
using System.Threading;
using Doka.EntityFrameworkCore.NestedSet;
using Doka.NestedSet;

var identity = typeof(NestedSetDbContext).Assembly.GetName().Name;

if (identity != "Doka.EntityFrameworkCore.NestedSet" || !new NestedSetBounds(1, 2).IsLeaf)
{
    throw new InvalidOperationException("Common EF contract did not load.");
}

await Console.Out.WriteLineAsync("Provider-independent EF contracts verified.".AsMemory(), CancellationToken.None);
