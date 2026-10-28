# Doka.NestedSet

ORM-independent nested-set bounds, node contracts, and in-memory ancestry
predicates. This package has no Entity Framework Core or database-provider
dependency.

## Install

```sh
dotnet package add Doka.NestedSet --version 10.1.0
```

Version `10.1.0` retains the stable ORM-independent API from `10.0.0`.

For database-backed hierarchies, install the matching
`Doka.EntityFrameworkCore.NestedSet.MySql`, `.Postgres`, `.Sqlite`, or `.SqlServer`
adapter instead. It brings in both common packages transitively. The
[getting-started guide](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/main/docs/getting-started.md)
covers EF mapping, native callback registration, queries, and mutations.

## Validated bounds

```csharp
using Doka.NestedSet;

var root = new NestedSetBounds(1, 6);
var child = new NestedSetBounds(2, 3);

var containsChild = root.Contains(child); // true
var rootWidth = root.Width; // 6
var descendantCount = root.DescendantCount; // 2 in a dense tree
var isLeaf = child.IsLeaf; // true
```

Bounds use positive `long` coordinates, require `Right > Left`, and require an
even inclusive width. The default struct value is invalid; calculations and
containment checks validate it. `Contains` is strict, so a node does not contain
itself. Bounds alone do not carry a tree identity: callers must ensure that both
intervals belong to the same tree. Local validation does not establish that an
entire persisted tree is valid or dense.

## Optional node contracts

`INestedSetNode<TNodeKey, TTreeId>` exposes `Id`, `TreeId`, `Left`, `Right`,
`Depth`, and `Position` as getters. Roots have depth zero; sibling positions are
zero-based. `IScopedNestedSetNode<TNodeKey, TTreeId, TScope>` also exposes `Scope`.
The scoped tree identity is the pair `(Scope, TreeId)`.

`IsAncestorOf` and `IsDescendantOf` compare complete tree identity before
containment, using `EqualityComparer<T>.Default` for identity values. Use the
scoped overload for scoped nodes; casting away the scope would discard part of
their identity. These are strict, synchronous in-memory predicates, without
database I/O. They validate the supplied bounds and reject null nodes.

EF mappings may use arbitrary entities without implementing either interface,
including mapped shadow properties. Database writes, query translation,
transactions, ordering, and structural repair belong to the EF packages.

The package is licensed under MIT.
