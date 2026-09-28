# Sibling ordering

Nested-set bounds encode preorder traversal. `Position` encodes the dense,
zero-based order among one parent's children. These values are
related but serve different purposes: bounds make subtree and ancestor queries
efficient, while position preserves an explicit adjacency order that can be
rebuilt and inspected directly.

Each TreeId has exactly one root. Its Position is always zero; roots are never
a shared sibling group across several trees.

## Manual and declarative models

An entity without `OrderBy` uses manual placement. Insert and move methods such
as `InsertBeforeAsync`, `MoveAfterAsync`, and `InsertAsFirstChildAsync`
determine the resulting position.

An entity with `OrderBy` uses declarative placement:

```csharp
builder.HasNestedSet(node => node
    .HasNodeKey(folder => folder.Id)
    .HasScope(folder => folder.TenantId)
    .HasTreeId(folder => folder.TreeId)
    .HasParent(folder => folder.ParentId)
    .HasBounds(folder => folder.Left, folder => folder.Right)
    .HasDepth(folder => folder.Depth)
    .HasPosition(folder => folder.Position)
    .OrderBy(folder => folder.Name)
    .HasOrderMode(NestedSetOrderMode.Strict));
```

Each sibling group is sorted independently. A preorder read therefore returns
parents before descendants while keeping every sibling group alphabetical.

```text
/
  bin
  home
    alice
    dominic
  var
```

Sorting the final flat result with
`InTree(treeId).Nodes.OrderBy(folder => folder.Name)`
does something different: it mixes nodes from every depth and destroys tree
traversal order. Configure sibling order in the model when hierarchical output
must be alphabetical.

## Composite criteria

Use the same shape as LINQ ordering:

```csharp
.OrderBy(folder => folder.Type)
.ThenByDescending(folder => folder.Name)
```

`OrderBy` or `OrderByDescending` starts a new criterion list. `ThenBy` and
`ThenByDescending` require an existing list and reject duplicate properties.
If the configured NodeKey is absent, the library appends it ascending as a
stable tie breaker.

The database decides comparison semantics. Effective collation, provider type
mapping, and configured value conversion apply. Nullable criteria require
explicit `NullSortOrder.First` or `NullSortOrder.Last`, so provider defaults do
not silently change ordering. Configure equivalent column types and collations
when separate environments must produce identical string order.

## Automatic placement on insert and move

With a configured order, broad operations derive the target position from the
entity's saved or supplied values:

- `InsertChildAsync` chooses the child position;
- `MoveToAsync` chooses the destination position; and
- bulk insertion sorts every new sibling group before assigning positions and
  bounds.

`InsertRootAsync` creates a new explicit TreeId, whose only root has Position
zero. `DetachAsTreeAsync` similarly creates a new tree rather than placing a
root beside roots from unrelated trees.

Generated-on-insert order values are read after insertion before final
placement. Values generated on update cannot safely determine a move before
the database has produced them and are unsupported as ordering criteria.

## Automatic reorder on SaveChangesAsync

Changing an order property and calling `SaveChangesAsync` repositions the
complete subtree in the same transaction:

```csharp
folder.Name = "Archive";
await context.SaveChangesAsync(cancellationToken);
```

The coordinator:

1. detects ordered hierarchy candidates;
2. resolves their original and destination scopes;
3. acquires locks in deterministic order;
4. saves the caller's payload changes once;
5. calculates the new sibling order from database values;
6. moves affected subtrees and refreshes tracked structure; and
7. accepts changes only after the complete boundary succeeds.

Changing Parent through a tracked property uses the same boundary. Direct
Scope or TreeId changes are rejected. For explicit application intent, facade
move methods remain clear and provide typed validation. Synchronous saves
reject changes that require this coordination.

Saves that contain no ordered hierarchy candidate follow EF Core's ordinary
path. Merely having one ordered entity type in the model does not place every
save behind a hierarchy transaction.

## Explicit placement modes

`NestedSetOrderMode.Strict` is the default. It rejects operations whose stated
placement can contradict the configured criteria:

- insert before or after;
- insert as first or last child; and
- move before or after.

Use `InsertRootAsync`, `InsertChildAsync`, `MoveToAsync`, and
`DetachAsTreeAsync` so the model chooses every applicable position.

Some domains need an automatic default plus occasional manual overrides.
Configure this deliberately:

```csharp
.OrderBy(item => item.Name)
.HasOrderMode(NestedSetOrderMode.AllowManualPlacement)
```

Manual operations then determine `Position` even when they do not match the
criterion values. A later order-property change, rebuild, or other automatic
reorder can restore criterion order. Applications should expose that behavior
to users instead of presenting manual placement as permanent.

## Rebuild behavior

`RebuildAsync` starts from persisted parent relationships. For an ordered
entity, it sorts every root and child group by configured criteria and then
recomputes positions, depth, and bounds. For a manual entity, existing valid
positions define sibling order; duplicate or invalid adjacency values are
reported rather than guessed.

## Concurrency and failure behavior

Reordering is a hierarchy mutation. It follows the same transaction and lock
protocol as insert, move, delete, and rebuild. A failure rolls back payload and
structure together when the database reports a definite rollback. A commit
failure can have an unknown outcome; do not retry blindly. Follow
[Transactions and locking](transactions-and-locking.md).

The application remains responsible for authorization and optimistic domain
concurrency. The exact tree-registry lock serializes structural writers; it
does not decide whether a caller may rename or move a domain entity.

## Choosing a model

| Requirement | Configuration |
| --- | --- |
| Always alphabetical folders | `OrderBy(folder => folder.Name)` in strict mode |
| Priority, then name | `OrderBy(item => item.Priority).ThenBy(item => item.Name)` |
| User-arranged dashboard | No configured order; use explicit placement |
| Alphabetical default with occasional pinning | Configured order plus `AllowManualPlacement`, with override semantics documented |

Choose one rule for a sibling group. Mixing hidden application-side sorting
with independently maintained positions creates output that cannot be rebuilt
or explained from persisted data.
