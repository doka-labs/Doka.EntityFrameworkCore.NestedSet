using System;
using System.Threading;
using Doka.NestedSet;

var root = new NestedSetBounds(1, 6);
var child = new NestedSetBounds(2, 3);

if (!root.Contains(child) || root.DescendantCount != 2 || !child.IsLeaf)
{
    throw new InvalidOperationException("Core package bounds contract failed.");
}

await Console.Out.WriteLineAsync("Core bounds and containment verified.".AsMemory(), CancellationToken.None);
