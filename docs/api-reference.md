# API reference

This reference describes the public stable `10.1.0` API, including the explicit
provider-package/registration break from `10.0.0`. The application keeps its normal
`DbSet<TEntity>` and obtains hierarchy behavior from the caller-owned context.

The current stable contract is recorded in the shipped baselines for
[Core](../src/Doka.NestedSet/PublicAPI.Shipped.txt) and
[EF Core](../src/Doka.EntityFrameworkCore.NestedSet/PublicAPI.Shipped.txt).
Each provider adapter records its native callback in its own shipped baseline:
[MySql](../src/Doka.EntityFrameworkCore.NestedSet.MySql/PublicAPI.Shipped.txt),
[Postgres](../src/Doka.EntityFrameworkCore.NestedSet.Postgres/PublicAPI.Shipped.txt),
[Sqlite](../src/Doka.EntityFrameworkCore.NestedSet.Sqlite/PublicAPI.Shipped.txt), and
[SqlServer](../src/Doka.EntityFrameworkCore.NestedSet.SqlServer/PublicAPI.Shipped.txt).
Unshipped files are reserved for changes after `10.1.0`.

The historical `10.0.0` declarations remain available in that release's
[Core baseline](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/v10.0.0/src/Doka.NestedSet/PublicAPI.Shipped.txt) and
[EF baseline](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/blob/v10.0.0/src/Doka.EntityFrameworkCore.NestedSet/PublicAPI.Shipped.txt).
SDK package validation still compares the two original package IDs with
`10.0.0`, with only the approved options-registration removal suppressed. See the
[breaking registration migration](upgrading-provider-packages.md).

## Registration and mapping

`UseNestedSets()` on the actual provider callback builder installs the shared
conventions, typed tree registry, save guard, relational persistence boundary,
diagnostics, and the selected adapter's native services. The overload receiver
is `MySqlDbContextOptionsBuilder`, `NpgsqlDbContextOptionsBuilder`,
`SqliteDbContextOptionsBuilder`, or `SqlServerDbContextOptionsBuilder`, from the
corresponding NestedSet adapter package. It returns the same native builder.
Registration is idempotent and safe for context pooling; conflicting adapters
and mismatched configured provider identities are rejected before hierarchy I/O.

The former `DbContextOptionsBuilder.UseNestedSets()` overloads are removed.
`ModelBuilder.UseNestedSets()`, `ModelConfigurationBuilder.UseNestedSets()` and
`HasNestedSet(...)` remain model configuration APIs.

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
| `HasAncestorIndex(string nonLeafPropertyName = "NestedSetIsNonLeaf")` | Opt into a generated Boolean nonleaf property and additive ancestor access index; requires a migration |
| `OrderBy(...)`, `ThenBy(...)` | Ascending sibling criteria |
| `OrderByDescending(...)`, `ThenByDescending(...)` | Descending sibling criteria |
| `HasOrderMode(...)` | Strict or manual-override placement |
| `PreserveApplicationConcurrencyTokens()` | Keep application-managed tokens unchanged during structure-only SQL |

Every structural role has a property-selector form. String forms support
field-only, indexer, and shadow properties. Nullable order criteria require an
explicit `NullSortOrder.First` or `NullSortOrder.Last`.

The ancestor-index option uses a property name rather than a selector because
its default creates a shadow property during model finalization. Property-bag
entities receive an indexer property with EF's natural missing-key sentinel;
callers do not initialize the generated dictionary entry. The option can adopt
compatible existing metadata, but does not make that generated value a
writable structural role.
It retains all existing structural and ordering indexes. See the
[configuration contract](configuration.md#optional-ancestor-index) and
[migration contract](migrations.md#optional-ancestor-index).
Schema availability does not activate a provider read optimization. Strict
indexed selection uses the MySQL adapter's directly bound Doka source binder;
inclusive selection also uses its ordered join. Other adapters retain their
portable query strategies and report schema-only ancestor-index use. See the [measured option trade-offs](qualification/query-composition.md#optional-ancestor-access).

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

All three query bindings, including `NestedSetTree<TEntity, TTreeId>`, also
expose these deferred compositors:

| Member | Contract |
| --- | --- |
| `SelectSubtrees(anchors, projection)` | Anchor, inclusive descendant, mapped depth difference |
| `SelectAncestors(nodes, projection)` | Source, strict ancestor, mapped depth difference |
| `SelectAncestorsOrSelf(nodes, projection)` | Source, ancestor or self, mapped depth difference |
| `WherePathEdges(source, anchor, node, edgeAllowed)` | Complete visible direct path, upper-exclusive and lower-inclusive |
| `WhereAnyAncestorOrSelf(source, node, predicate)` | Visible ancestor/self existence, without a complete-path requirement |
| `WhereNearestAncestorOrSelf(source, node, candidate, predicate)` | Nearest candidate first; value and complete visible path afterward, without fallback |

The projection is `Expression<Func<TEntity, TEntity, int, TResult>>`.
Path predicates receive `(row, directParent, directChild)`; owner predicates
receive `(row, owner)`. Endpoints select existing mapped aliases from a query
owned by the same EF context. Each filter preserves the source bag and adds no
ordering. No method performs I/O or accepts a cancellation token: the async EF
terminal operation owns execution and cancellation. See
[Query Composition](query-composition.md) for visibility, error, tracking,
identity, rank, and mixed-direction contracts.

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
