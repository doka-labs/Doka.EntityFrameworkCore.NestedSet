# API reference

This reference describes the public 10.x API. The application keeps its normal
`DbSet<TEntity>` and obtains hierarchy behavior from the caller-owned context.

The initial `10.0.0` contract is recorded in the shipped baselines for
[Core](../src/Doka.NestedSet/PublicAPI.Shipped.txt) and
[EF Core](../src/Doka.EntityFrameworkCore.NestedSet/PublicAPI.Shipped.txt).
These declarations are unchanged from the accepted RC API.

## Registration and mapping

`DbContextOptionsBuilder.UseNestedSets()` installs the conventions, typed tree
registry, save guard, relational persistence boundary, provider services, and
diagnostics. It is idempotent and safe for context pooling.

`EntityTypeBuilder<TEntity>.HasNestedSet(...)` maps one hierarchy entity. The
builder supports these structural roles:

| Method | Purpose |
| --- | --- |
| `HasNodeKey(...)` | Stable scalar node identity |
| `HasTreeId(...)` | Stable tree identity |
| `HasScope(...)` | Optional application partition |
| `HasParent(...)` | Nullable direct parent key |
| `HasBounds(...)` | Inclusive `long` left and right boundaries |
| `HasDepth(...)` | Zero-based `int` depth |
| `HasPosition(...)` | Dense zero-based `long` sibling position |
| `OrderBy(...)`, `ThenBy(...)` | Ascending sibling criteria |
| `OrderByDescending(...)`, `ThenByDescending(...)` | Descending sibling criteria |
| `HasOrderMode(...)` | Strict or manual-override placement |
| `PreserveApplicationConcurrencyTokens()` | Keep application-managed tokens unchanged during structure-only SQL |

Every structural role has a property-selector form. String forms support
field-only, indexer, and shadow properties. Nullable order criteria require an
explicit `NullSortOrder.First` or `NullSortOrder.Last`.

Without configured criteria, sibling order is manual. With criteria,
`NestedSetOrderMode.Strict` makes automatic insert and move placement follow
those criteria and rejects explicit first, last, before, and after placement.
`NestedSetOrderMode.AllowManualPlacement` permits those explicit operations
alongside configured ordering.

## Context entry points

`context.NestedSet<TEntity>()` returns an unscoped facade after validating the
final EF model. `context.NestedSet<TEntity>(entityTypeName)` selects a named
shared-type entity. A scoped hierarchy must then call `ForScope(scope)` before
any query or mutation.

`SaveNestedSetChangesAsync(...)` composes tracked Parent and order-property
changes into an application context's async save override. The synchronous
`SaveNestedSetChanges(...)` accepts payload-only work and rejects hierarchy work
that requires database coordination. `NestedSetDbContext` provides the same
composition as an optional base class.

## Query facade

`NestedSet<TEntity>` and `ScopedNestedSet<TEntity, TScope>` expose the same
query surface:

| Member | Result | Default order |
| --- | --- | --- |
| `InTree(treeId).Nodes` | One exact tree | Left preorder |
| `TreeContaining(nodeKey)` | Tree containing the visible anchor | Left preorder |
| `SubtreeOf(nodeKey)` | Anchor and descendants | Left preorder |
| `ChildrenOf(nodeKey)` | Direct children | Position |
| `DescendantsOf(nodeKey)` | Strict descendants | Left preorder |
| `AncestorsOf(nodeKey)` | Strict ancestors | Root to parent |
| `ParentOf(nodeKey)` | Direct parent | Zero or one row |

Each member returns a composable, no-tracking `IQueryable<TEntity>`. Anchor
resolution remains in the generated SQL; no API requires loading the anchor
entity first. Public queries respect application query filters and always add
the required Scope and TreeId predicates.

## Mutation facade

Both facades expose immediate asynchronous operations:

| Group | Members |
| --- | --- |
| Create | `InsertRootAsync`, `InsertChildAsync`, `InsertAsFirstChildAsync`, `InsertAsLastChildAsync`, `InsertBeforeAsync`, `InsertAfterAsync` |
| Move | `MoveToAsync`, `MoveBeforeAsync`, `MoveAfterAsync`, `DetachAsTreeAsync` |
| Delete | `DeleteAsync`, `DeleteSubtreeAsync`, `DeleteTreeAsync` |
| Bulk | `InsertSubtreeAsync`, `InsertForestAsync` |
| Administration | `PurgeTreeIdAsync` |

`InsertRootAsync` and `DetachAsTreeAsync` require an explicit never-used
TreeId. Child, sibling, and anchor-based operations resolve the complete tree
identity in the database. Every async method accepts a `CancellationToken`.

`NestedSetBranch<TEntity>` is an immutable adjacency input for one detached
branch. `NestedSetTreeImport<TEntity, TTreeId>` pairs one root branch with its
explicit TreeId for `InsertForestAsync`.

`PurgeTreeIdAsync(treeId)` is an explicit administrative operation. It accepts
only an existing tombstone with no remaining nodes, removes that registry row
atomically, and thereby permits deliberate later TreeId reuse. Applications
must authorize purge separately and retain any required business audit before
calling it.

## Tree-bound maintenance

`InTree(treeId)` returns `NestedSetTree<TEntity, TTreeId>`:

- `Nodes` is the complete composable tree query;
- `ValidateAsync(level, token)` returns an immutable
  `NestedSetValidationReport`;
- `PlanRebuildAsync(token)` returns an immutable, write-free
  `NestedSetRebuildPlan`; and
- `RebuildAsync(token)` atomically reconstructs bounds, depth, and position.

`NestedSetValidationLevel.Quick` checks bounded structural invariants.
`NestedSetValidationLevel.Full` reconciles complete adjacency, order, depth,
and interval state. Issues use `NestedSetValidationCode`; rebuild plans expose
repairability, node counts, batch count, affected `NestedSetStructuralRole`
values, and issues. Full validation and rebuild plans retain at most 1,024
individual issues. `TotalIssueCount`, `IssueCounts`, and `IssuesTruncated`
report complete counts and whether node keys were omitted. Tree-wide issues,
including a missing root, have a null `NodeKey` and a stable code. The legacy
`ValidateDetailedAsync` service method still returns every node issue and can
therefore use memory proportional to the number of corrupt nodes.

## Core package

`Doka.NestedSet` has no EF Core dependency:

- `INestedSetNode<TNodeKey, TTreeId>` defines conventional unscoped fields;
- `IScopedNestedSetNode<TNodeKey, TTreeId, TScope>` adds Scope;
- `NestedSetBounds` validates one inclusive interval; and
- `NestedSetNodeExtensions` compares complete tree identity before evaluating
  ancestry or descent.

The interfaces are optional. Explicit EF mapping is the primary compatibility
path for existing domain entities.

## Failures and diagnostics

`NestedSetException.Code` carries a stable `NestedSetErrorCode`:

| Code | Meaning |
| --- | --- |
| `OperationRejected` | A general operation precondition failed |
| `NodeNotFound` | A required anchor is absent from the selected Scope |
| `CycleDetected` | A parent relationship would create a cycle |
| `InvalidStructure` | Stored hierarchy invariants are invalid |
| `InvalidContext` | Tracking or save integration violates the contract |
| `InvalidTransaction` | Transaction, retry, isolation, savepoint, or SQL Server `XACT_ABORT` state is invalid |
| `ManualPlacementNotAllowed` | Explicit placement conflicts with strict order |
| `InvalidImport` | Bulk topology or staged entity state is invalid |
| `LockAcquisitionFailed` | The typed registry could not establish the required lock |
| `TreeNotFound` | The exact Scope and TreeId have no active registry row |
| `TreeIdUnavailable` | The requested TreeId is active or tombstoned |
| `TreeIdNotTombstoned` | Administrative purge selected an active TreeId |
| `ConcurrentTreeIdentity` | A resolved anchor moved to another tree before its mutation lock |

Provider errors, cancellation, concurrency exceptions, argument validation,
and overflow retain their native types when a library wrapper would hide useful
semantics.

`NestedSetDiagnostics.ActivitySourceName` and
`NestedSetDiagnostics.MeterName` both equal
`Doka.EntityFrameworkCore.NestedSet`. See [Diagnostics](diagnostics.md) for the
bounded telemetry contract.

## Ownership

Facades, contexts, tracked entities, and builders are not thread-safe. The
application owns context lifetime, query execution, authorization, caller
transactions, commit, domain audit data, and recovery after an unknown commit
outcome.
