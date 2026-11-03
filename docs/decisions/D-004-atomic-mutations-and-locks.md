---
id: D-004
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Mutation atomicity, caller transactions, typed tree locks, retries, and failure ownership"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-004 -- Serialize writers through typed per-tree registry locks

## Context and Problem Statement

A nested-set mutation changes intervals, adjacency, positions, registry state,
and sometimes two trees. Concurrent writers that calculate gaps from different
snapshots can corrupt both trees. Applications also need hierarchy work to
commit atomically with domain writes and may configure retrying EF execution
strategies.

## Decision Drivers

- Serialize writers for the same complete tree identity.
- Permit independent server writers for unrelated trees.
- Preserve application ownership of an existing transaction and final commit.
- Lock both sides of cross-tree operations in one stable database order.
- Recheck changed nodes and Parent endpoints after locking before saving payload.
- Support retries only around the complete repeatable application transaction.
- Preserve definite rollback and expose unknown commit outcomes.

## Considered Options

- Typed registry row per Scope and TreeId with one transaction protocol
- Table-wide or Scope-wide writer lock
- Optimistic revision checks with isolated automatic retries
- Application-owned raw SQL lock coordination

## Decision Outcome

Chosen option: "Typed registry row per Scope and TreeId with one transaction
protocol", because database equality and ordering must follow the configured
store types, converters, and collations. A caller transaction is protected by a
savepoint and remains caller-owned. Without one, the library owns the complete
transaction. A coordinated save checks every changed node's current tree
identity after acquiring its planned locks, before persisting payload. On SQL
Server, a caller transaction with `XACT_ABORT ON` is rejected before the
savepoint: a caught duplicate-key error can otherwise make the whole
transaction uncommittable, so a savepoint could not preserve earlier work.

### Consequences

- Good, because same-tree writers serialize while independent server trees do not share a library-level global lock.
- Good, because hierarchy and domain writes can commit atomically in one application transaction.
- Bad, because a hot tree remains a serialized write partition and nested-set interval shifts are still O(N) in the database.
- Bad, because SQLite retains its database-level writer limit despite the tree-local public contract.
- Bad, because SQL Server caller mutations require one session-option read and reject `XACT_ABORT ON`; applications using that mode must turn it off before the hierarchy call.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx --filter "FullyQualifiedName~RetryBoundaryTests|FullyQualifiedName~TreeRegistry|FullyQualifiedName~Concurrent|FullyQualifiedName~Transaction"` and expect first-writer races, same-tree serialization, distinct-tree overlap, counter-direction cross-tree moves, caller rollback, cancellation, and unknown commit cases to pass on every qualified provider.
- The retry-boundary cases must reject a transaction created outside the configured execution-strategy delegate and pass when the caller creates its transaction inside the active complete-unit delegate.
- The SQL Server caller-transaction cases must reject `XACT_ABORT ON` before a hierarchy write and show that earlier caller work can still commit. A coordinated save must reject a node that moved to an unlocked tree before payload persistence.

## Pros and Cons of the Options

### Typed registry row per Scope and TreeId with one transaction protocol

- Good, because typed database identity avoids CLR hash, formatting, and collation disagreement.
- Bad, because each hierarchy adds an internal migration table and tombstone lifecycle that operators must monitor.

### Table-wide or Scope-wide writer lock

- Good, because the identity and acquisition protocol are simple.
- Bad, because unrelated trees block one another and cannot meet the independent-writer target.

### Optimistic revision checks with isolated automatic retries

- Good, because low-contention writers might avoid holding a lock during planning.
- Bad, because repeating only structural SQL can duplicate or detach it from domain side effects, and an unknown commit cannot be made safe without application idempotency.

### Application-owned raw SQL lock coordination

- Good, because an expert application could optimize for a known deployment.
- Bad, because every consumer would have to reproduce provider locking, first-tree creation, savepoint, rollback, and tombstone semantics.

## More Information

Registry keys are `TreeId` or `(Scope, TreeId)`. Revision changes with
structure; Lifecycle preserves active and tombstoned identities. Cross-tree
operations ask the database to order typed identities before acquisition.
Unless `HasTreeRegistryTable` names it explicitly, the registry's physical table
name derives from the hierarchy schema, table,
and identity column names. CLR entity renames therefore leave the registry
table intact. Databases created with the earlier CLR-name-based hash require
the data-preserving table rename described in the migration guide.
Rollback cleanup uses `CancellationToken.None` after forward progress fails.
This prevents request cancellation from suppressing recovery, not provider
rollback timeouts. SqlClient 6.1.6's native transaction rollback uses the
connection timeout rather than EF's ordinary command timeout. Applications
must budget recovery for their largest transaction. If rollback or disposal
fails, preserve both errors, discard the context and caller transaction, and
reconcile through a fresh connection; client timeout does not prove server undo
has finished. Capacity fixtures remove this implicit undo-speed gate only for
their functional tests, retaining ordinary command deadlines and every exact
rollback assertion.
A connection loss during commit requires a fresh-context reconciliation. The
only supported registry-row deletion is `PurgeTreeIdAsync`: it follows the same
transaction and exact-identity lock protocol, rejects active identities, and
deletes a tombstone only after verifying that no hierarchy rows remain.

Insertion recovery owns the exact introduced root and owned entries, including
an initial transition rejected by an application tracking callback. An entry
can remain Detached while EF already holds a reference or only some of its key
registrations. Reassigning Detached cannot release those registrations.
Callbacks can also replace or mutate a key after its native map installation.

Single insertion completes public detachment before the owned commit or caller
savepoint release. A rejecting `StateChanging` or `StateChanged` callback can
therefore roll back the INSERT. Recovery restores captured generated CLR
leaves on the root, complex values, and owned payload. It also restores owned
keys and ownership foreign keys captured before EF's relationship fixup,
including assigned keys whose `ValueGenerated` is `Never`. Ordinary payload
and non-ownership business foreign keys remain caller-owned. Initialization
first retains the bounded owned graph, so a throwing application getter or tracking callback cannot leave a
previously introduced owned reference behind. Caller-tracked foreign entries
are never treated as entries introduced by the insertion.

The shared insertion lifecycle uses comparer snapshots and exact-entry native
removal for those cases. It captures generated identities at the existing
relational persistence boundary, before acceptance or `SavedChanges`; the
managed path verifies CLR and sidecar representations before acceptance can
hide an edit. Ordinary detachment remains the first lifecycle step. Exceptional
cleanup removes only the introduced entry's surviving reference, key, and
dependent memberships, preserving unrelated entries and legitimate collisions.

This is a deliberate EF infrastructure seam: public state assignment cannot
remove a never-tracked reference or an installed key whose representation was
changed by a callback. The required contracts are qualified against EF
10.0.12, resolved once, and used through cached delegates in the normal path.
One model-free binding owner separates these contracts from entry lifecycle
state. `UseNestedSets` validates every required signature, including closed
scalar, nullable-key, and actual `IReadOnlyList<object?>` composite map shapes,
before registering the extension. Metadata validation does not compile unused
probe operations; native closed map operations compile on first model use and
remain weakly cached. Incompatibility names the loaded EF version and exact member before
any context writes; it does not require discarding a nonexistent context.
An explicit contract test localizes framework changes instead of relying on
many insertion failures to reveal them.
They are not covered by EF's public compatibility promise. When database
rollback succeeds, failed identity restoration still produces the existing
aggregate cleanup error and discard-context instruction. Clearing the caller's
tracker, synthetic lifecycle transitions, and global map scans are rejected
alternatives because they alter unrelated application state or callback
behavior.

Unchanged identity matching and detach preparation reuse the same typed readers
as refresh. Current values alone are insufficient: change detection can install
an intermediate relationship key even when a callback subsequently restores
the original CLR value. The native generic relationship-snapshot reader is
therefore validated once and compiled once per property reader. EF's public
`IUpdateEntry` route exposes only an object snapshot getter; that public route
remains appropriate when capturing actual changed vectors. Differing CLR and
model member types keep EF's object conversion semantics, including sentinels.
Typed comparer expressions preserve null protection for both operands.
Dependent-bucket storage is allocated only for a real native membership.
The initial-root and warmed comparison budgets qualify these isolated paths;
they do not replace complete-import heap or hosted capacity evidence.

The same binding owner supplies the read-only save guard with EF's exact
`StateManager.TryGetEntry(object, IEntityType, bool)` reference lookup. It is
qualified from the supported EF 10.0.12 minimum and validated at registration.
The lookup creates no entry and performs no detection; callers exclude detached
registrations. Exact target metadata preserves named/shared and derived maps.
The public update adapter has no equivalent reference lookup. Building a fresh
index of every tracked entity for one navigation change would add linear
allocation and work unrelated to that relationship, so that alternative is
rejected. The unchanged guard path does not need this lookup.

Read-only hierarchy protection also applies when automatic detection is disabled
throughout a coordinated save or facade insertion. It does not discover pending
application payload. Parent/order plans capture only their affected candidates;
managed insertion reuses its bounded staged write set. Both still inspect relevant
navigation effects on all tracked entries. Public typed update-entry reads and
model-lifetime comparers preserve shadow and generated sidecar semantics. The
existing complete tracker snapshot remains the rollback boundary for coordinated
saves. Its relationship portion additionally preserves installed FK identities,
pending CLR references, collection instances and list order independently from
native snapshot membership. Normal scalar restoration alone would run fixup and
normalize caller edits that were deliberately not detected.

The existing registration-time framework binding validates three native operations:
`SetRelationshipSnapshotValue(IPropertyBase, object)`,
`AddToCollectionSnapshot(INavigationBase, object)` and
`RemoveFromCollectionSnapshot(INavigationBase, object)`. Compiled delegates restore
scalar/reference snapshots and collection membership without per-operation
reflection. Collection snapshots use reference-set semantics; preserving an absent
private sidecar rather than an empty container is not a public rollback contract.
Required-FK conceptual nulls and scalar resets can leave equal scalar snapshots
with different native dependent-bucket membership. Neither scalar equality nor
the final null flag proves presence. The same binding therefore validates
`StateManager.GetDependents(IReadOnlyList<object>, IForeignKey)`, compiling its exact call once. This read
lets EF initialize its own lazy dependent cache when necessary; its indexed
set lookup captures actual membership without enumerating a wide owner per node.
The public update adapter cannot perform this key-only read for untracked principals.
Existing scalar traversal repairs changed callback identities. After installed vectors settle, recovery
restores membership in that exact indexed vector. A sparse second journal retains
a different pending current vector only for already divergent FKs; its membership
is restored after raw current/sidecar/CLR values and null flags return. Both journals
mutate only EF's existing indexed `ICollection<IUpdateEntry>` at the explicit vector.
Native entry-based Add/Remove interpret conceptual-null flags, and Update removes
the installed bucket, so none can restore these two independent preimages alone.
A trial extra `StateManager.UpdateDependentMap` call became redundant after exact-vector
restoration. Its omission preserved all 3,181 Unit cases in isolation; the final
implementation removes that call, its binding and its two obsolete signature tests.
An incompatible immutable/missing captured-present bucket rejects recovery rather
than fabricating transient shared-FK values. No new tracker or service replacement
is introduced. Native bucket and inverse-navigation absence remain independent.
Notification rollback also validates EF's three native property/collection event
handler signatures at registration. Closed delegates bind the exact native entry
only during recovery through cached `MethodInfo.CreateDelegate`; wrapper lambdas
would not match the delegates already subscribed by EF. A bounded scope removes
only EF's handlers from the owner and distinct current/captured collections,
restores raw storage, then restores subscriptions in `finally`. Application
listeners remain installed. No handler lookup or invocation through reflection
is added to successful writes.

The full framework `Unsubscribe`/`SnapshotAndSubscribe` pair is not selected:
both can call `GetOrCreateCollection` and change a valid captured null collection.
Direct exact handler suspension preserves null, avoids reattaching detached
inputs through notification fixup, and handles an old collection still subscribed
under `ChangedNotifications`. It changes no entity state and resets no sidecar.
FK identities and endpoint navigation preimages are captured for every entry
owned by the rollback snapshot, including initially unchanged FKs. Callbacks can
otherwise hide native fixup behind a raw reset of an equal scalar. Each endpoint
is copied once. Existing pending/lifecycle relationships retain full graph
authority; supplemental unchanged relationships restore dependents with changed
installed identities or explicit FK writes at the captured identity, preserving
unrelated unmarked CLR edits and deliberately unmarked round trips.
Changed Parent domains share one pre-save scan for preexisting affected
memberships; supplemental captures do not suppress wrong-parent cleanup.
Cleanup materializes owners before
notification-based mutation can invalidate EF's live entry iterator.
Traversal is linear in tracked potential owners and relevant edges. Retained
navigation state is proportional to captured collection membership, including
wide source/destination collections, not merely the number of changed nodes.
Supplemental ordered recovery uses reference-index chains and a linear merge,
including duplicate occurrences. Its scratch and affected-reference sets exist
only during rollback. If pending raw anchors conflict with original interleaving,
the selected original sequence and unselected pending sequence each retain their order.
Receipts retire only after all restoration checks succeed,
so repeated recovery cannot remove a restored original member again.

Insertion records proven saved external-owner inverse memberships in one
operation-local journal across batches and forest plans. A prepared CLR member
does not make EF add that member to its snapshot again, so a successful insertion
must baseline that proven relationship explicitly. Failure restores the original
membership even when the unchanged owner has no callback writes. A distinct lazy
reference index can authorize the complete validated import in owner collections,
but never enlarges the active batch's allowed persistence set. Ordinary imports
without such collections do not allocate this additional whole-plan index.

Recovery audits only the distinct touched maps once for exact introduced
entries that survived cleanup. This failure-only read uses EF's native map
enumeration, with bounded active-batch memory and linear time in the entries
of those maps. It detects stale hash slots after a callback explicitly rekeys
a mutable identity through change detection and mutates it again. Such a slot
cannot be repaired through exact key operations; it produces the aggregate
recovery failure and discard-context requirement, preserving the original
guard error and definite database rollback. The successful insertion path does
not enumerate these maps.

Whole-entity Unchanged restoration is not used to undo flagless sharing-peer
promotion: it rejects complex originals and can replace pending containers.
The public PK false-flag path finishes an unchanged entity without requiring a
non-key scalar or writing private state flags.

Complex graph recovery retains CLR containers and ordered element references
independently of the native current and original element lanes. Restore parent
containers before scalar or ordinal access, and restore retained complex
authorities before accepted Added/Deleted owner transitions can create new
entries with empty originals. Capture both registered identities and null slots:
original-only Deleted entries and raw unregistered Detached entries are distinct.
A callback can trim an Added identity out of its lane without changing its state.
Verification therefore checks actual slot identity, state and both ordinals,
not a fresh collection-index lookup alone.
Current and original lane lengths and null holes are independent captured
authority too. Parent reactivation must not traverse stale child registrations.
After temporary access storage is removed, native lanes must have their captured
lengths, including pending Added children; an extra staging slot can otherwise
break the next save despite an apparently successful first recovery. These
lengths must not be normalized to a raw-shortened current CLR count.

Capture also defines ownership of native complex occurrences. Retained
identities, flags and ordinals in that preimage remain authoritative, including
public ComplexElementEntry handles belonging to it. A native occurrence first
created after capture is not a retained tracker authority. If its CLR ancestors
have already disappeared, a normal recursive detach would visit that invalid
path. Recovery retires only those uncaptured cache subtrees through EF's normal
capacity operation, without reading their lost CLR path. A captured descendant
under an uncaptured owner is rejected rather than silently evicted; EF's native
ContainingEntry ownership is immutable in the qualified versions.

These complex occurrences use EF's snapshot complex caches, not the global
entity identity map or entity notification registrations. Cache retirement
therefore does not assign private state/flag fields, clone caller entities or
introduce another framework binding. Handles for native occurrences first
created after capture are invalid after that rollback and must be reacquired
from the restored graph. Their old State is not promised to read Detached.
This does not remove entries
already included in a managed save's later callback-write snapshot. The actual
capture boundary, rather than the fact that a callback created an object,
determines whether an identity belongs to the retained preimage.

Recovery covers composed callback changes, including repeated removal and
addition followed by reordering, pending Deleted elements with nested collections
and pending Added children of a removed owner, rather than treating one mutation
as the only possible callback shape. Native retirement and reinstallation use
separate access graphs, because live and captured ordinals differ. Direct
Detached-to-Deleted transitions must not create new children: EF gives those no
original buffer, and reading their nested collections overflows the stack, which
no caller could catch. Nested originals stay hidden during each transition.
A Deleted owner borrows an addressable slot until its descendants have settled. Unchanged containers are compared before indexed
writes or mapped setter calls. Read-only/immutable IList storage and copying
property setters therefore do not require a write for no-op recovery. Required
struct-reference scalar and primitive-collection restoration follows EF's
mapped value comparers; a later replay of an old boxed struct must not overwrite
those independent scalar snapshots. Struct containers are replayed unconditionally,
because their Equals is caller code that can ignore members. EF cannot build a
containing-entity setter through Nullable<T>, so those scalars copy back through
their retained containers. Field-mapped collections are staged by assignment, so
temporary null slots reach no caller collection code.

Native children hidden by a raw-null or shortened current parent retain scalar
originals, modification flags and available old CLR instances independently.
Their scalar intent is restored while temporary containers expose the native
path; final verification does not require an invalid CurrentValue read after
the original null/shortened parent returns.
For a struct reference inside a retained original element, verification resolves
its declaring instance again after mapped copyback. A previously boxed struct
can be stale even though the original element remains the correct authority.
Struct collection elements remain unsupported by EF and do not require an
alternative recovery branch.

An entity without complex properties owns no topology journal or empty
complex-state list. If native lanes and hidden scalar preimages remain
compatible, rollback avoids native rebinding and replays captured containers
and independent originals. Otherwise, existing captured/original/live instances
provide a temporary access graph for
EF's callbacks, using collection storage from EF's mapped factory. Cleanup and
re-registration use EF's capacity, ordinal and lifecycle operations; aggregate
flags use its nonrecursive method. Temporary-container receipts exist only on
this failure path. They undo original/live container changes before final
raw-null replay hides those paths, then are cleared after restoration. The exact
captured CLR graph replaces the temporary graph. Field/property access modes
and valid struct references use EF's mapped containing-entity setter; runtime fixed-size collections restore by
slot under a supported collection mapping. EF's model rejection of struct
collection elements remains unchanged.

The complex adapter validates cached native-lane fields for read-only access.
The ordinary entry-list getter cannot substitute: it initializes capacity by
traversing CLR membership and can fail on an intentionally raw-shortened graph.
The adapter also closes exact infrastructure, mapped setter/factory and native
state/ordinal/flag signatures once for the loaded EF assembly. No private native
lane assignment or private state/flag write is used. The delegate bundle
initializes when complex capture first needs its native readers, including on
successful saves. A signature incompatibility in that capture-initialized bundle
therefore rejects capture before the first persistence command; it is not a
post-write recovery failure.
Its native write operations are invoked only during rollback.
Automatic or explicit DetectChanges would consume the caller's unmarked preimage,
so it is not a recovery primitive. Versioned source comparison covers EF 10.0.12 and 10.0.14;
runtime qualification uses 10.0.14 and does not qualify every permitted patch.

The original-buffer binding is necessary for a raw-null complex leaf that
must retain a distinct original and an unmodified flag. The public false setter
copies the original through its absent CLR container; the public original setter
would detect the restored difference again. Recovery temporarily aligns that
one original while its flag is true, clears the public flag, then writes the
independent original through InternalEntryBase's existing OriginalValues.SetValue.
The shared once-compiled writer accepts IPropertyBase metadata for both scalar
and complex-collection originals. It validates the native buffer,
EnsureOriginalValues initializer and SetValue signatures and reuses the exact
IInfrastructure native-owner reader. Scalar callers pass public metadata
directly; the former reflected PropertyEntry.Metadata getter and duplicate
collection writer are removed. Native writes run only during rollback. EF still
owns state transitions and notifications; native state/flag fields are not written.
Writing a raw-null leaf through a caller-retained original instance can mutate
it, and arbitrary object construction/materialization would impose unproven constructor or read-model
constraints. Neither is selected. A separate incompatibility first encountered
by this rollback-only writer can fail recovery after definite database rollback;
the existing aggregate error and discard-context requirement apply there.
The [manual-detection recovery contract](../configuration.md#manual-change-detection)
keeps capture-time incompatibility and rollback-only failure distinct.

### Re-evaluation Triggers

- EF changes its cached complex collection array/list fields, metadata slot
  index, mapped setter/indexed factory, native ordinal/capacity methods or
  nonrecursive collection flag semantics. Requalify post-capture shrink/null,
  original-only Deleted and unregistered Detached slots, trimmed Added orphans,
  detected reorder, accepted owner lifecycles, raw pre-save removals, duplicate
  references, exact lane lengths/holes after composed mutations, pending Added
  nested children, unchanged read-only/copying-setter mappings and comparer-aware
  struct primitive-collection replay. Requalify deep callback-only cache
  retirement, preserved captured handles and post-capture handle invalidation
  if EF changes complex snapshot strategy, global tracking or owner immutability.
  Compare complete capture and exceptional recovery separately before upgrading.
- EF changes the exact native-owner infrastructure interface, original-buffer
  field, initialization or OriginalValues.SetValue signature/semantics. Requalify
  raw-null complex leaves
  with present/absent originals, reference and nonnullable value-type leaves,
  hidden marked children through acceptance, inherited metadata, native element
  state, exact flags and external caller-instance preservation.
- EF changes complex element slot initialization, detached ordinal handling,
  aggregate flag recursion or same-row command traversal. Requalify raw complex
  add/remove/replacement, nested promotion, callback peers and independent EF
  command-preparer membership before upgrading.
- EF changes table-sharing peer promotion, flagless Modified state, or the public
  Modified-to-Unchanged original-value rejection. Requalify OwnsOne and ordinary
  table splitting with genuine concurrency, completion/structural failure, raw
  payload, shared readback, Added/Deleted state and identical retry.
- EF changes inverse collection fixup when a raw duplicate already exists.
  Requalify post-receipt native-only recovery, original presence/absence,
  unmarked CLR ordering and complete FK capture/native repair at 20,000 members.
- A supported provider changes row-lock, transaction, savepoint, or execution-strategy behavior.
- A provider changes native rollback timeout or cancellation behavior. Requalify full-size late failure and cancellation with actual database, registry, input and tracker restoration, retaining recovery failure classification.
- Query-plan evidence shows a registry statement takes locks outside its exact identity.
- A tested idempotency and commit-verification contract makes optimistic replay safe for a complete application unit.
- EF changes reference registration, identity-map or dependent-map cleanup, generated-value acceptance, or the insertion lifecycle contracts. Requalify initial tracking rejection, mutable and generated keys, owned payload, unrelated identities, retry, and definite database rollback before upgrading.
- EF introduces a public typed relationship-snapshot getter or changes its native generic signature, composite key shape, sentinel conversion, or comparer null handling. Revisit the reader seam and rerun signature, allocation, intermediate-key, sidecar, property-bag, and exact cleanup controls.
- EF introduces an equivalent public reference lookup or changes native reference-map lookup, detached navigation
  fixup, target assignability, or shared-map identity. Requalify ordinary payload saves, tracked/untracked navigation
  deltas, equal-key principal replacements, and the constant unchanged-path allocation budget.
- EF changes native relationship snapshot setters, inverse collection baselining, or notification fixup.
  Requalify pending graph rollback, map membership, prepared owners, cross-batch failure and identical retry.
- EF exposes a public explicit-vector dependent-map repair API or changes the key-only lookup signature, lazy-map
  initialization, indexed mutable-set representation, conceptual-null key creation or required-FK fixup. Requalify late and
  preexisting conceptual nulls, scalar/unmarked round trips, unchanged native references, composite/shared FKs,
  native absence, raw-current versus installed-vector preimages, cold/warm allocation, repeated recovery and
  exact binding rejection before upgrading. Do not substitute an immutable or linear-scan collection silently.
- EF changes the native notification handler signatures, subscription identity or collection replacement rules.
  Requalify all three notification strategies, null/replaced collections, surviving application listeners,
  restored EF subscriptions and absence of detached-input reattachment before upgrading.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: The proposal documented the earlier shared writer boundary for retrospective comparison.
- 2026-09-23: Typed per-tree registry locks, database-ordered cross-tree acquisition, caller savepoints, and complete-unit retry ownership were confirmed.
- 2026-09-23: Status changed from proposed to accepted.
- 2026-09-23: Documented physical registry naming and its one-time legacy table-rename path after the CLR-rename regression review.
- 2026-09-24: Saves recheck changed nodes after locking and reuse the outer boundary for Parent moves.
- 2026-09-24: Required `XACT_ABORT OFF` in SQL Server caller transactions after verifying that `ON` can invalidate savepoint rollback on a caught duplicate-key error.
- 2026-09-28: The maintainer confirmed acceptance and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed typed registry locks, caller savepoints, complete-unit retries, tracker recovery, and provider regression specifications against the linked repository evidence.
- 2026-10-04: Qualified exact insertion identity recovery after initial tracking rejection and callback key mutation. Preserved the existing persistence boundary, caller tracker ownership, and explicit framework-seam failure policy.
- 2026-10-04: Separated insertion framework bindings from lifecycle state and moved their cached compatibility check to options registration, with member-specific startup diagnostics and an explicit framework-contract regression.
- 2026-10-04: Diagnosed SqlClient's native connection-timeout boundary during million-row undo. Kept runtime recovery and aggregate error preservation unchanged; separated the functional capacity fixture from implicit rollback timing gates and documented application-owned recovery budgets.
- 2026-10-08: Removed unchanged-path boxing and eager bucket storage, preserved installed relationship snapshots and native property-bag diagnostics, and separated metadata-only startup validation from model-consumed delegate compilation. Added allocation, semantic, lazy-cache, and actual registration controls.
- 2026-11-01: Reused the validated framework binding for side-effect-free exact reference lookups in navigation guards, preserving detached-removal and equal-key principal semantics without indexing the complete tracker.
- 2026-11-01: Extended the validated binding for independent relationship restoration and proven saved inverse baselining; separated full-import observation permission from bounded active-batch write authorization.
- 2026-11-01: Real notification controls rejected snapshot-slot access and undo-induced reattachment. Chose exact recovery-only handler suspension to preserve null/replaced collections and application subscriptions.
- 2026-11-02: Added supplemental pre-save endpoint evidence for initially unchanged FKs, retaining selective raw-graph recovery, ordered duplicates and native absence. Successful recovery retires inverse receipts; supplemental captures cannot suppress wrong-parent cleanup. Complete-capture allocation is qualified separately.
- 2026-11-02: A required-FK control exposed equal scalar snapshots with missing dependent buckets. Extended the existing exact binding with key-only native map lookup; captured actual indexed membership independently of scalar/null flags, including sparse divergent-current preimages. Qualified cold/warm allocation, preexisting nulls, explicit indexed-set compatibility and marked/unmarked scalar round trips.
- 2026-11-02: Removed the trial native update binding after omission controls and complete isolated Unit verification showed no remaining consumer. Exact-vector sets retain both installed and pending bucket preimages.
- 2026-11-02: Restore flagless sharing-peer promotion through the existing early state transition before scalar/sidecar/CLR replay. Recover native-only inverse deltas after receipt undo without consuming pending CLR order. Verified real provider faults, positive paths, retry and 20,000-member resource controls.
- 2026-11-02: Complex-peer regressions replaced whole-entity rejection with non-rejecting flags, child-first native slot restoration and the narrow original-buffer binding for raw-null complex leaves. Managed callback capture now includes exact unchanged sharing peers. Retained previous resource bounds and native EF semantics.
- 2026-11-02: Callback shrink/null regressions required a distinct CLR topology and current/original native-slot journal, accepted-owner ordering and exact orphan re-registration. Documented cached native-lane reads and validated mapped/native methods; retained manual detection, existing guards and ordinary resource ceilings.
- 2026-11-02: Unified scalar/collection original writes, retained hidden native-child scalar intent and old CLR instances, and retired failure-only staging receipts before final raw-null replay. Added inherited-metadata, optional nested-container and explicitly detected native-rebind controls.
- 2026-11-02: Composed callback review extended the recovery contract to exact lane lengths, pending Added children, unchanged read-only/copying-setter storage and comparer-aware struct primitive collections. Distinguished capture-time reader incompatibility from failure in the rollback-only original writer.
- 2026-11-02: Separated native retirement from reinstallation after a pending Deleted element overflowed the stack during rollback. Added borrowed ordinals, hidden nested originals, staged entity transitions, nullable struct copyback and field-mapped staging without caller collection mutation.
- 2026-11-02: Added explicit per-hierarchy registry table names. The internal registry entity name stays independent of the table name, so a change diffs as a rename that keeps all identities and tombstones.
- 2026-11-02: Clarified operation-relative ownership for callback-only complex cache occurrences whose CLR ancestors disappeared. Captured authority remains exact; uncaptured cache handles require reacquisition after rollback. Hidden struct reads resolve their retained original element after mapped copyback.
- 2026-11-03: Consolidated public recovery contracts, regression references and resource guidance in their maintained guides. Removed development-stage report dependencies; decision and runtime contracts are unchanged.

### Implementation References

- [Manual-detection recovery contract](../configuration.md#manual-change-detection)
- [Recovery implementation and framework boundary](../implementation-design.md#ordering-and-coordinated-saves)
- [Recovery and native-authority regressions](../regression-coverage.md#manual-detection-and-hierarchy-guards)
- [Capture and recovery resource model](../performance.md#prepared-imports-and-recovery-costs)
- [Complex topology journal](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetComplexSnapshot.cs)
- [Hidden native-child scalar recovery](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetComplexSnapshot.HiddenProperties.cs)
- [Validated complex entry adapter](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetComplexEntryAccess.cs)
- [Recovery-only complex original buffer](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetComplexOriginalValue.cs)
- [Mutation executor](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetMutationExecutor.cs)
- [Transaction boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTransaction.cs)
- [Tree locks](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetTreeLocks.cs)
- [Bounded insertion identity lifecycle](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionTracking.cs)
- [Insertion framework binding contract](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionContract.cs)
- [Affected relationship recovery](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetRelationshipSnapshot.cs)
- [Saved inverse membership journal](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetCollectionBaseline.cs)
- [Recovery-only notification scope](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetNotificationPause.cs)
- [Insertion-owned CLR rollback values](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionValues.cs)
- [Insertion comparison and capture budgets](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Comparisons.cs)
- [Insertion recovery semantics](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/InsertionRefreshAllocationTests.Recovery.cs)
- [Insertion startup and native map contracts](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/InsertionReflectionContractTests.cs)
- [Provider-result boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetRelationalDatabase.cs)
- [Single insertion callback regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert)
- [Single detachment rollback and retry](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.Detachment.cs)
- [Single initialization cleanup](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.Initialization.cs)
- [Legitimate identity collision recovery](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/SingleInsertGuardTests.Collision.cs)
- [Bulk native identity regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkIdentityRetentionTests.cs)
- [Failed tracking recovery regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkStagingRetentionTests.cs)
- [Typed registry convention](../../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetInfrastructureConvention.cs)
- [Tree identity purge](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Maintenance/NestedSetTreeRegistryPurger.cs)
- [Retry tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Transactions/RetryBoundaryTests.cs)
- [Tree-registry concurrency tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/TreeRegistry)
- [Tree identity purge tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Mutations/Facade/NestedSetMutationFacadeTests.Purge.cs)
- [Registry rename tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/TableMappings/RegistryMigrationProviderTests.cs)
- [Transaction guide](../../docs/transactions-and-locking.md)
- [Full-size failure and cancellation controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Capacity/CapacityTests.cs)
- [Registry migration guide](../../docs/migrations.md)

### Sources

- [EF reference-preserving original collection snapshot](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/SnapshotFactoryFactory.cs#L382-L396) (primary source; retrieved 2026-11-02)
- [EF public property index contract](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Metadata/IPropertyBase.cs) (primary source; retrieved 2026-11-02)
- [EF complex tracking versus entity identity maps](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/StateManager.cs#L511-L558) (primary source; retrieved 2026-11-02)
- [EF cached complex collection storage and capacity operations](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs#L1105-L1218) (primary source; retrieved 2026-11-02)
- [EF complex current/original registration and lifecycle](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.InternalComplexCollectionEntry.cs#L31-L151) (primary source; retrieved 2026-11-02)
- [EF complex ordinal and state validation](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalComplexEntry.cs#L105-L257) (primary source; retrieved 2026-11-02)
- [EF mapped containing-entity setter](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Metadata/IClrPropertySetter.cs#L20-L48) (primary source; retrieved 2026-11-02)
- [EF mapped struct copyback](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Metadata/Internal/ClrPropertySetterFactory.cs#L180-L337) (primary source; retrieved 2026-11-02)
- [EF runtime setter and indexed collection factory](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Metadata/Internal/IRuntimePropertyBase.cs) (primary source; retrieved 2026-11-02)
- [EF complex collection element validation](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Infrastructure/ModelValidator.cs#L346-L350) (primary source; retrieved 2026-11-02)
- [EF public flag reset and scalar rejection](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs#L432-L524) (primary source; retrieved 2026-11-02)
- [EF public original setter detection](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs#L960-L990) (primary source; retrieved 2026-11-02)
- [EF direct comparer-snapshotted original-buffer writer](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.OriginalValues.cs#L31-L55) (primary source; retrieved 2026-11-02)
- [EF exact native member owner](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/MemberEntry.cs#L37-L50) (primary source; retrieved 2026-11-02)
- [EF shared-row peer promotion](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.Relational/Update/Internal/CommandBatchPreparer.cs#L321-L352) (primary source; retrieved 2026-11-02)
- [EF public provider state setter](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L117-L120) (primary source; retrieved 2026-11-02)
- [EF public state transition](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/EntityEntry.cs#L75-L87) (primary source; retrieved 2026-11-02)
- [EF original-value rejection on state restoration](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs#L283-L320) (primary source; retrieved 2026-11-02)
- [EF shared generated-column readback](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.Relational/Update/ColumnModification.cs#L160-L176) (primary source; retrieved 2026-11-02)
- [EF Core 10.0.14 dependent-map update](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/StateManager.cs#L1020-L1029) (primary source; retrieved 2026-11-02)
- [EF Core 10.0.14 lazy dependent-map lookup](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/StateManager.cs#L1068-L1074) (primary source; retrieved 2026-11-02)
- [EF Core 10.0.14 indexed dependent-map membership](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/DependentsMap.cs) (primary source; retrieved 2026-11-02)
- [EF Core 10.0.12 native notification handlers](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L866-L982) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.14 collection replacement handlers](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L866-L901) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.14 full subscriber collection initialization](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntrySubscriber.cs) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.12 relationship snapshot operations](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L533-L587) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.14 relationship snapshot membership](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.RelationshipsSnapshot.cs) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.12 exact reference lookup](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/StateManager.cs#L356-L364) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.14 tracked navigation fixup](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/NavigationFixer.cs) (primary source; retrieved 2026-11-01)
- [EF Core public update adapter](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.update.iupdateadapter?view=efcore-10.0) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.12 public object relationship-snapshot getter](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/IUpdateEntry.cs#L156) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 native typed relationship-snapshot getter](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L514) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 composite principal-key factory](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/CompositePrincipalKeyValueFactory.cs#L12) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 key type selection](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/IKey.cs#L30) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 sentinel-aware property access](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/PropertyAccessorsFactory.cs#L236) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 CLR getter conversion](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/ClrPropertyGetterFactory.cs#L164) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 sentinel null guards](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Extensions/Internal/ExpressionExtensions.cs#L56) (primary source; retrieved 2026-10-08)
- [EF Core 10.0.12 comparer null guards](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/ValueComparer%60.cs#L239) (primary source; retrieved 2026-10-08)
- [SqlClient 6.1.6 native rollback timeout selection](https://github.com/dotnet/SqlClient/blob/b862260e80dd3d159b812cbe5c51b933fe348afe/src/Microsoft.Data.SqlClient/netcore/src/Microsoft/Data/SqlClient/SqlInternalConnectionTds.cs#L1190-L1193) (primary source; retrieved 2026-10-04)
- [EF Core 10.0.12 native rollback forwarding](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalTransaction.cs#L209-L254) (primary source; retrieved 2026-10-04)
- [.NET default async rollback cancellation behavior](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbtransaction.rollbackasync?view=net-10.0) (primary source; retrieved 2026-10-04)
- [EF Core 10.0.12 state manager and reference lifecycle](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/StateManager.cs) (primary source; retrieved 2026-10-03)
- [EF Core 10.0.12 exact identity-map removal](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/IdentityMap.cs) (primary source; retrieved 2026-10-03)
- [EF Core value comparers and key snapshots](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers) (primary source; retrieved 2026-10-03)
- [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) (primary source; retrieved 2026-09-23)
- [EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency) (primary source; retrieved 2026-09-23)
- [MySQL locking reads](https://dev.mysql.com/doc/refman/8.4/en/innodb-locking-reads.html) (primary source; retrieved 2026-09-23)
- [PostgreSQL explicit locking](https://www.postgresql.org/docs/current/explicit-locking.html) (primary source; retrieved 2026-09-23)
- [PostgreSQL Read Committed](https://www.postgresql.org/docs/current/transaction-iso.html) (primary source; retrieved 2026-09-24)
- [SQLite isolation](https://www.sqlite.org/isolation.html) (primary source; retrieved 2026-09-23)
- [SQL Server table hints](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table) (primary source; retrieved 2026-09-23)
- [SQL Server TRY...CATCH and uncommittable transactions](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/try-catch-transact-sql) (primary source; retrieved 2026-09-24)
- [SQL Server SET XACT_ABORT](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-xact-abort-transact-sql) (primary source; retrieved 2026-09-24)
- [EF Core migration customization](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing) (primary source; retrieved 2026-09-23)
