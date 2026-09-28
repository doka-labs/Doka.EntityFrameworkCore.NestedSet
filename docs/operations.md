# Queries and mutations

The standard entry point is a lightweight facade over the caller-owned
`DbContext` and normal entity set. The examples use the `Folder` from
[Getting started](getting-started.md):

```csharp
var folders = context.NestedSet<Folder>()
    .ForScope(tenantId);
```

The facade validates NodeKey, Scope, and TreeId argument types against the
final EF model. It is not thread-safe and is not an authorization boundary.
Await each operation before using the context again.

For an unscoped hierarchy, omit `ForScope(...)`:

```csharp
var folders = context.NestedSet<Folder>();
```

## Composable queries

Queries resolve the structural anchor in SQL and return no-tracking
`IQueryable<TEntity>` values:

```csharp
var nearestMatchingAncestor = await folders
    .AncestorsOf(nodeId)
    .Where(folder => folder.Type == "system")
    .OrderByDescending(folder => folder.Depth)
    .FirstOrDefaultAsync(cancellationToken);
```

This does not load `nodeId` first. The composed query remains inside the bound
Scope and the anchor's TreeId.

| Query | Membership | Default order |
| --- | --- | --- |
| `InTree(treeId).Nodes` | Complete explicit tree | Left preorder |
| `TreeContaining(nodeKey)` | Complete tree containing visible anchor | Left preorder |
| `SubtreeOf(nodeKey)` | Anchor and visible descendants | Left preorder |
| `ChildrenOf(nodeKey)` | Visible direct children | Position |
| `DescendantsOf(nodeKey)` | Visible strict descendants | Left preorder |
| `AncestorsOf(nodeKey)` | Visible strict ancestors | Root to parent |
| `ParentOf(nodeKey)` | Visible direct parent | At most one row |

A missing or filtered anchor yields an empty result. Application predicates
filter result nodes; they do not alter structural anchor resolution. Call
`AsTracking()` explicitly when a result must participate in a later tracked
payload or coordinated hierarchy save.

## Create nodes

`InsertRootAsync` requires a stable, never-used TreeId:

```csharp
var treeId = Guid.NewGuid();
var root = new Folder("Root", "system");

await folders.InsertRootAsync(root, treeId, cancellationToken);
```

Child and sibling operations infer TreeId from their database anchor:

| Method | Placement |
| --- | --- |
| `InsertChildAsync(entity, parent)` | Configured order, or last manual child |
| `InsertAsFirstChildAsync(entity, parent)` | Explicit first child |
| `InsertAsLastChildAsync(entity, parent)` | Explicit last child |
| `InsertBeforeAsync(entity, sibling)` | Immediately before sibling |
| `InsertAfterAsync(entity, sibling)` | Immediately after sibling |

The entity must be detached and must not carry populated non-owned
navigations. NestedSet assigns Scope, TreeId, Parent, bounds, depth, and
position. Assigned and generated scalar NodeKeys are supported. On success,
the inserted graph is detached with final generated and structural values.

Strict configured ordering rejects explicit placement methods. Use
`InsertChildAsync` so the model determines position.

## Move nodes and trees

| Method | Result |
| --- | --- |
| `MoveToAsync(node, parent)` | Move complete subtree beneath parent using configured/default order |
| `MoveBeforeAsync(node, sibling)` | Move before same-tree sibling in manual/flexible mode |
| `MoveAfterAsync(node, sibling)` | Move after same-tree sibling in manual/flexible mode |
| `DetachAsTreeAsync(node, newTreeId)` | Normalize subtree as the root of a new tree |

`MoveToAsync` may cross TreeIds inside the same Scope. It locks source and
destination, updates TreeId on the complete subtree, closes and opens the
intervals, updates depth and position, and tombstones a source TreeId when its
root was merged. Cross-Scope moves are rejected.

Moving below self or a descendant is rejected as a cycle. Explicit mutation
requires affected hierarchy rows to be untracked because set-based SQL would
otherwise leave stale entities. Unaffected trees and ordinary domain entity
types may remain tracked when they have no pending incompatible changes.

## Delete nodes and trees

`DeleteAsync(nodeKey)` deletes one non-root node and promotes its direct
children into its sibling group. It rejects a root because promoting its
children would create multiple roots under one TreeId.

`DeleteSubtreeAsync(nodeKey)` removes one complete branch. When the selected
node is the root, it deletes the tree and tombstones its registry identity.

`DeleteTreeAsync(treeId)` is the explicit identity-based whole-tree operation.
It does not require loading the root.

For TPT and split table mappings, deletion removes the dependent physical
fragments before their principals in the same transaction. Subtree and tree
deletion clear internal parent references before bounded delete batches, so a
restrictive self-reference remains valid even between batches. A hierarchy
mapped to one table, including inline `OwnsOne` values, retains EF's set-based
deletion path when it is the principal of that table.

These immediate database deletes do not synchronize EF's change tracker.
Previously tracked table-split payload entities for removed rows must be
discarded or reloaded before further use, just like tracked dependents of an
`ExecuteDelete` cascade.

Deletion preserves the TreeId as a tombstone. Reuse requires a separate,
authorized administrative action:

```csharp
await folders.PurgeTreeIdAsync(treeId, cancellationToken);
```

The purge locks the exact registry identity and rejects an active, missing, or
damaged tombstone. It also verifies through an unfiltered tree query that no
nodes remain. After the purge transaction commits, a later `InsertRootAsync`
may deliberately use the TreeId again.

The library does not invent domain cascades. Referencing application rows need
an explicit Restrict, reassign, or delete policy in the same caller-owned
transaction.

## Bulk import

`NestedSetBranch<TEntity>` describes detached adjacency.
`InsertSubtreeAsync(branch, parentNodeKey)` imports beneath an existing parent.
`InsertForestAsync(trees)` creates multiple explicit trees atomically:

```csharp
var imports = new[]
{
    new NestedSetTreeImport<Folder, Guid>(
        firstTreeId,
        new NestedSetBranch<Folder>(firstRoot, [new(firstChild)])),
    new NestedSetTreeImport<Folder, Guid>(
        secondTreeId,
        new NestedSetBranch<Folder>(secondRoot)),
};

await folders.InsertForestAsync(imports, cancellationToken);
```

Every root has an explicit TreeId. The entire input is validated before the
first hierarchy write. Planning is iterative and writes use bounded batches
inside one atomic boundary. See [Bulk import](bulk-import.md).

## Tracked Parent and order changes

When the context uses `NestedSetDbContext` or the composed save extensions,
ordinary tracked changes can update hierarchy placement:

```csharp
var folder = await context.Folders
    .SingleAsync(candidate => candidate.Id == folderId, cancellationToken);

folder.ChangeParent(newParentId);
folder.Name = "Archive";

await context.SaveChangesAsync(cancellationToken);
```

The coordinator plans from original and current values, locks every affected
tree in stable order, calls the application's EF save once, runs structural
mutations, refreshes coordinates and store-generated concurrency values, and
then accepts changes. A concurrency conflict rolls back the complete boundary.

## Validation and rebuild

Maintenance belongs to one exact tree:

```csharp
var tree = folders.InTree(treeId);
var report = await tree.ValidateAsync(
    NestedSetValidationLevel.Full,
    cancellationToken);

var plan = await tree.PlanRebuildAsync(cancellationToken);

if (!report.IsValid && plan.CanRebuild)
{
    await tree.RebuildAsync(cancellationToken);
}
```

`NestedSetValidationReport` is immutable and contains level, node count, valid
state, and typed issues. `NestedSetRebuildPlan` is a read-only dry run with
repairability, node count, changed-node count, bounded batch count, and changed
structural roles.

Rebuild uses Parent and sibling order as canonical data. It cannot repair a
missing parent, cycle, duplicate identity, or ambiguous domain intent.

## Atomicity and retries

Every mutation is atomic. The library owns a transaction when none exists and
uses a savepoint inside a compatible caller transaction. It never commits or
disposes the caller transaction.

When EF retries are configured, invoke the execution strategy manually and
start the caller transaction inside its delegate. The delegate must include all
related hierarchy and domain writes. A transaction created outside that retry
delegate is rejected before hierarchy SQL.

Cancellation applies to forward progress. Rollback and cleanup use
`CancellationToken.None` once failure handling begins so an already canceled
request cannot abandon local transaction cleanup.

## Failure contract

Stable library failures use `NestedSetException` and `NestedSetErrorCode`:

| Code | Meaning |
| --- | --- |
| `NodeNotFound` | Required anchor does not exist in the selected Scope |
| `TreeIdUnavailable` | Requested new TreeId already exists or is tombstoned |
| `TreeIdNotTombstoned` | Administrative purge selected an active TreeId |
| `ConcurrentTreeIdentity` | A pre-lock anchor lookup became stale after a concurrent tree move; resolve the anchor again before deciding whether to retry |
| `CycleDetected` | Requested parent is self or descendant |
| `InvalidStructure` | Stored hierarchy invariants are invalid |
| `InvalidContext` | Tracking or save integration violates the contract |
| `InvalidTransaction` | Ambient, retry, isolation, savepoint, or SQL Server `XACT_ABORT` state is invalid |
| `ManualPlacementNotAllowed` | Explicit position conflicts with strict order |
| `InvalidImport` | Bulk topology or staged entity state is invalid |
| `LockAcquisitionFailed` | Tree registry could not establish the required lock |
| `OperationRejected` | A stable operation precondition failed |

Provider failures, cancellation, concurrency exceptions, argument validation,
and arithmetic overflow keep their native exception types when wrapping them
would discard useful semantics. A commit error can have an unknown outcome;
discard the context and reconcile through stable application identity.
