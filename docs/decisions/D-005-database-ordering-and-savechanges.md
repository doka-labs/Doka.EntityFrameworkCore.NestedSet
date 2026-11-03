---
id: D-005
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Sibling order modes, database comparisons, and SaveChanges coordination"
supersedes: []
superseded-by: []
amends: []
amended-by: [D-012]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-005 -- Integrate configured ordering with asynchronous saves

## Context and Problem Statement

Alphabetically ordered folders need sibling sorting without flattening the complete tree. Renaming a tracked folder
can change its position and subtree coordinates. The implementation offers manual, flexible, and strict order modes
and coordinates configured ordering with SaveChangesAsync.

## Decision Drivers

- Preserve preorder for full-tree reads and database comparison semantics for sibling keys.
- Allow applications to choose manual placement, flexible overrides, or a strict rule.
- Commit payload edits and structural reorder together.
- Preserve relevant EF tracker, callback, and acceptance semantics after success or failure.

## Considered Options

- Database ordering with explicit asynchronous save integration
- Read-time sorting in application memory
- Explicit reorder calls after every payload change

## Decision Outcome

Chosen option: "Database ordering with explicit asynchronous save integration", because the configured rule is a
persistence contract, and payload changes must not leave Position and intervals inconsistent. Mode selection keeps
manual placement available when requested.

### Consequences

- Good, because renaming a node can update its sibling position and complete subtree order in the same transaction as the payload.
- Bad, because configured saves cost more than a native payload-only EF save and require the documented asynchronous context integration.

An inherited hierarchy is coordinated through its exact configured owner,
including changed and tracked sibling derived types. Grouping by concrete
entry metadata would restrict Parent resolution and structural updates to one
subtype and cannot apply an ordering selector declared on the common owner.
The owner is cached with the EF model; named shared mappings and separately
configured concrete stores remain distinct.

Refresh metadata includes generated concurrency tokens declared by participating
descendants. Scalar projections cast to the declaring subtype and guard its
discriminator, rather than loading payload or assigning a sibling's token.
The same projection primitive supports native refresh, explicit mutation, and
detached insertion. Tracker refresh preserves pending payload and original-value
semantics when SaveChanges uses `acceptAllChangesOnSuccess: false`.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx -c Release --filter "FullyQualifiedName~Ordering|FullyQualifiedName~SaveChanges|FullyQualifiedName~PostCommitOutcomeTests"` and expect all selected tests to pass.
- Require equal sort keys, database collation, manual flexible placement, rename reorder, callback-introduced changes, rollback restoration, and `SaveChangesAsync(false, cancellationToken)` coverage. Expect unsupported synchronous ordered saves to reject before partial structural work.
- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx -c Release --filter "FullyQualifiedName~ManagedSaveCallbackTests|FullyQualifiedName~SingleInsertGuardTests|FullyQualifiedName~OptionsExtensionTests"` and expect the persistence boundary on every provider, in both extension orders, with post-save inspection, audit writes, suppression rejection, and restored accepted callback writes after rollback.
- Run `OrderingTrackerTests.LateFailureRestoresAddedIdentityAndAllowsRetry` and `ManagedSaveCallbackTests.RolledBackCallbackWritesCanBeSavedAgain` on all five providers. The first covers a temporary key after a coordinated save fails; the second must persist restored Added, Modified, and Deleted callback entries after an insertion fails.
- Run `ManagedHierarchyPayloadTests` on all five providers. Payload writes to an unaffected tree must commit or restore with the insertion, while a structural write must fail before persistence.
- Run the mixed TPH/TPT managed-save regressions on every provider. Cross-type
  Parent changes, dependent cross-tree moves, base ordering, tracked refresh,
  both acceptance modes, and rejected or failed saves must preserve the full
  configured hierarchy rather than only the changed concrete subtype.

## Pros and Cons of the Options

### Database ordering with explicit asynchronous save integration

- Good, because the database compares mapped values and coordinated saves keep payload and hierarchy changes atomic.
- Bad, because the coordinator needs locks, tracker snapshots, refresh queries, and additional writes for affected intervals.

### Read-time sorting in application memory

- Good, because persisted hierarchy operations remain simpler and display order can vary per caller.
- Bad, because the application must load enough hierarchy to reconstruct traversal, and partial filtered results may not contain the required parents.

### Explicit reorder calls after every payload change

- Good, because structural changes remain visible in application code and a specialized caller can batch them.
- Bad, because callers can forget the reorder or commit a renamed node with stale structural order.

## More Information

A global OrderBy on a flattened result cannot preserve parent-before-descendant traversal. The implementation
maintains sibling positions and bounds, then `InTree(...).Nodes` returns preorder. Saves without hierarchy work
retain EF's ordinary detection setting. The record does not claim an interceptor alone supplies coordination.

Ordinary payload saves must not enable detection merely because a hierarchy is mapped. The guard observes only
hierarchy roles, configured ordering properties, and relevant navigation snapshots. Cached typed comparisons use
EF's value comparers and do not mark entries, perform fixup, or detect unrelated tracked principals. Local
`EntityEntry.DetectChanges()` is not an alternative because EF also visits tracked FK principals from that entry.
Key/FK probes additionally observe EF's installed relationship identity through the existing validated typed
snapshot reader, so replacing a scalar's OriginalValue cannot hide a changed relationship. Typed comparer calls
retain EF's null guards before invoking application equality expressions.
Parent/order coordination and library-owned single/bulk/forest insertion also preserve the caller's detection
setting. With detection disabled, only explicitly recorded Parent/order changes may enter the coordinated plan;
raw unrecorded hierarchy edits reject. Added application entries, explicit property marks, notifications, and
application detection calls keep their normal EF semantics. Unmarked snapshot-tracked payload is not discovered.
The original candidate references survive temporary Parent suppression. A bounded plan independently snapshots
all their hierarchy/order values and flags, while every other hierarchy entry and all relevant navigation effects
remain read-only guarded. A flags-only candidate recount is insufficient: suppression can remove Parent-only
candidates and undetected callback changes need not set any flag. No new public option or provider seam is needed.
The plan is captured before suppression. Its authorized Parent target drives navigation-effect checks, while
the physical Parent snapshot is refreshed after suppression. Matching caller-maintained Parent references and
inverse memberships are therefore valid; a callback that chooses a different target still rejects. Rollback
retains the pending graph and native relationship snapshots independently, as specified in D-004.
Candidate snapshots independently retain current EF values, available originals, and CLR member values. A generated
sidecar must not conceal a later raw CLR key mutation. Initially captured divergence remains valid when unchanged;
shadow properties have no CLR storage. Ordinary probes inspect CLR storage only when an actual sidecar can mask it.
Both paths reuse the metadata-selected typed CLR getter used by insertion identity tracking, without another native
framework seam. Explicit application detection or a callback that enables it remains an EF request; the guard does
not undo that request, and its scope restores the original setting afterward.

Navigation observation follows EF's persistence effect: detached removed collection members are ignored because
EF can permanently retain them in its relationship snapshot, while added untracked hierarchy members remain
guarded. Principal replacement uses relevant FK ordinals and principal-key comparers; an equal-key replacement
does not become hierarchy work merely because its CLR reference differs. Exact tracked-reference lookup uses the
existing validated EF boundary documented in D-004. No tracker-wide index or entry creation is required.
Composite relationship effects use EF's minimal non-overlapping nulling subset, cached from public metadata with
principal/dependent ordinals preserved. An ordinary nullable owner-id change does not request hierarchy work merely
because Scope occurs elsewhere in that composite FK. Required-dependent deletion and real guarded FK changes still
reject. Clearing a dependent navigation can affect its FK even after the old principal was detached.
Required cascading or key-only relationships remain lifecycle-relevant even when their FK is entirely ordinary
payload. Unique owner replacement checks the existing dependent bucket for a hierarchy victim whose severance
can delete it; an unused owner without guarded FK changes remains ordinary payload.
The native unchanged collection comparison can box a nonempty List enumerator. Its measured 40-byte cost per
collection/pass has a separate populated-collection allowance; the existing empty-collection budget is unchanged.
Preserve native reference-set semantics. Active imports stream prepared collections
using operation-local tracked-removal scratch; ordinary unchanged probes retain
their qualified native comparison. D-007 records the remaining live-inspection cost.

For every registered relational save, prepared inverse collections are baselined only from FK
write evidence captured at the existing persistence boundary before SQL. Ordinary
written values use independent comparer snapshots; untouched members use original
values. Written temporary FKs await the principal's generated-key propagation;
expected generated members require an actual provider readback.
Apply that evidence only after provider success and before `SavedChanges`.
Unmarked payload and later command/completion callback edits cannot become proof
of an earlier write. A procedure may receive only original-value parameters even
for marked properties; require a complete returned FK vector for procedure proof.
Ambiguous untouched shared-column members and shared-identity transitions do not
create a new baseline: EF's existing membership and the strict guards remain
authoritative. This adds no model exclusion or EF reflection binding.
Before the library installs membership during a coordinated save, the existing tracker snapshot records
only that owner/navigation/member addition. A whole late-owner copy would capture changes already made by
`SavingChanges` through EF fixup and mistake them for pre-save state. Recovery removes only library-owned
additions before restoring proven original native memberships. This requires pre-save FK endpoint
preimages even for initially unchanged relationships; a matching current FK alone does not prove membership.
It preserves pending CLR membership independently. If a raw FK reset hides a different installed identity,
the existing native setter repairs the dependent map via that installed value; original comparison values
and modification flags are restored afterward. This traversal reuses the already bound scalar setter.
Required-FK conceptual nulls can leave equal scalar snapshots with a missing native bucket.
Their targeted map restoration uses the existing exact framework binding described in D-004;
the public update adapter cannot write that bucket. Captured pending null intent is preserved.
A lazily initialized map can also index a previously pending raw current vector independently of its installed
snapshot. Such divergent FKs retain a sparse second indexed-membership preimage, restored after current/CLR
values and flags return without removing the independently restored installed bucket.
The additional unchanged-FK preimages are supplemental: select native identity changes and explicit writes
at the captured identity before rollback setters run, then restore their references and original occurrences.
Completed marked native round trips retain their original list position; deliberately unmarked round trips
and unrelated late raw CLR
additions/removals remain pending. Previously changed FK/lifecycle captures retain their complete graph
authority, including when a supplemental navigation is promoted by another pending relationship.
Ordinary asynchronous, synchronous and direct registered saves use the same
persisted-FK proof; they retain EF's native transaction behavior and gain no coordinated rollback guarantee.

Unique-dependent lookup uses EF's native bucket. Parent belongs only to its exact
self-FK, and its conventional unique index is rejected as mutable structure.
For an authorized Parent with a tracked old principal, selected nulling pairs already compare the intended
Parent. When the old principal is absent or detached, those pairs cannot compare it: resolve the intended
FK vector using the existing update adapter, including the principal mapping compatibility check. Limit
that vector to this case; never mistake suppressed physical Parent for the requested relationship.
Other FKs retain the native principal
check because minimal-overlap selection can omit another changed FK member.

The coordinated save snapshots every tracked entry when hierarchy candidates are present. EF saves the context's
pending entries together, and save callbacks can change entries that were unchanged when planning began. A later
hierarchy failure must restore their original tracker state as well as the database transaction. Selecting only the
currently affected trees would therefore lose unrelated pending or callback changes. This cost is explicit and
bounded by the [tracker allocation regression](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackerSnapshotAllocationTests.cs);
applications should keep the context's tracked graph small for latency-sensitive hierarchy saves. See the
[performance contract](../../docs/performance.md) for the measured threshold.

Library-owned insertions save through EF's ordinary pipeline, so application save callbacks still run.
`UseNestedSets()` replaces the provider's standard `RelationalDatabase` service with a subclass. Each actual
persistence attempt receives its exact pending write set after `SavingChanges` callbacks and final change
detection and before the first command. The subclass validates the planned hierarchy entries there, admits ordinary
audit or outbox writes, and snapshots only those writes. EF accepts them with the insertion, so a later rollback in
the same insertion restores their pending tracker state from that snapshot. Post-save callbacks and tracker
inspection after the save are never validated against the plan. Registration rejects a provider-specific
`IDatabase` implementation instead of replacing it; SQL Server, SQLite, Npgsql, and Doka MySQL register the
standard service. The boundary adds no per-command interception. Outside managed insertion, it also validates
ordinary/coordinated hierarchy guards after callbacks and cascade processing, without changing the prepared write
set. The injected completion interceptor checks zero-write saves that never reach `IDatabase`, before application
completion callbacks run. Guards and temporary validation delegates are restored after both success and failure.
No additional detection-event guard scan is needed: the persistence boundary observes late detection callbacks
and cascade transitions, while the completion guard covers the zero-write fast path. Client-side cascade deletion
of tracked hierarchy nodes is rejected before SQL and requires explicit facade deletion.

For library-owned insertions, the managed save admits payload-only changes on already-tracked nodes in unaffected
trees, using the same structural and ordering property classification as coordinated saves. It rejects additions,
deletions, and changes to hierarchy-owned or configured ordering properties outside the planned insertion before
persistence begins. Every insertion uses an exact TreeId; there is no alternate scope-wide mutation surface.
Prepared external-owner collections can reference future detached inputs from the complete validated import.
Exact mapping, reference and bound identity still have to prove the relationship. This permission ends with the
operation and never admits future Added entries to the active batch's write set. Successful saved memberships
are baselined through the D-004 journal, so a following ordinary save does not mistake them for new additions.

The same recovery boundary owns post-capture complex container changes. A
coordinated snapshot precedes application save callbacks; a managed insertion
captures its actual callback-selected write set at persistence. These are
different preimages and remain different. Callback removal, null or replacement
must not hide the original failure behind an ordinal-access recovery error.
The D-004 complex journal restores caller containers and independent current and
original native authorities before scalar replay, including accepted owner
lifecycles. Hidden native children retain scalar originals, flags and available
old CLR instances; recovery restores them while temporary paths are reachable,
then undoes failure-only container receipts before final null replay. It
preserves unregistered raw additions instead of treating a later
provider-created Added entry as their original state. Exact pending graphs,
native slots and unrelated captured entries remain recoverable without an
implicit DetectChanges pass or relaxed structure authorization.

Confirmation combines direct tracker controls with ordinary, owned and split
provider writes, genuine concurrency and completion/later structural faults,
identical retry and successful persistence. Managed single, subtree and forest
cases exercise their actual capture boundary. Complete capture and exceptional
recovery resource checks remain separate from metadata/warmup and successful-save
measurements. The [regression inventory](../regression-coverage.md#manual-detection-and-hierarchy-guards)
maps these contracts to concrete controls. The [support matrix](../support-and-qualification.md#provider-and-engine-matrix)
keeps native SQL Server and hosted qualification boundaries explicit.

EF keeps a generated temporary key in a tracker sidecar while the entity's CLR key remains at its sentinel.
After EF accepts an insertion, restoring both values requires the EF temporary-value setter: the public
`PropertyEntry.IsTemporary` setter copies the current value into the CLR member. The snapshot resolves the EF
setter once, only on rollback, and the generated-key callback tests exercise restoration and a later save on
all supported providers. This internal EF contract must be checked when upgrading EF Core.

### Re-evaluation Triggers

- EF changes callback/persistence/acceptance order or trims/recreates complex
  entry slots differently. Requalify callback shrink/null/replacement at both
  coordinated and managed capture boundaries, unregistered versus registered
  additions, current/original authority and accepted Added/Deleted owners.
  Retain real database rollback, retry and complete resource observations.
- EF changes same-row promotion or native JSON ordinal creation/serialization
  without DetectChanges. Requalify plain EF and coordinated behavior together,
  preserving scalar/manual-detection semantics and exact failed pending state.
- EF changes table-sharing command preparation or flagless peer state promotion.
  Requalify unchanged and already Modified peers separately, preserving current,
  original, generated/temporary and raw values before verifying exception identity.
- EF changes native inverse fixup around duplicate CLR members or saved-addition
  undo. Requalify native-only membership independently from pending CLR order,
  including originally absent members and 20,000-entry capture/resource controls.
- A supported provider cannot translate a configured selector with the documented equality and ordering behavior.
- Measured refresh or snapshot cost exceeds an agreed workload budget.
- EF changes SaveChanges callback, acceptance, or generated-value semantics.
- EF changes original-value availability, scalar comparer semantics, relationship snapshot storage, or the ordering
  of injected/application save interceptors around the zero-write fast path.
- EF changes minimal-overlap FK selection, principal-value comparison, reference stealing, or required dependent
  severance. Requalify overlapping ordinary FK edits and genuine Scope/Parent/delete effects together.
- EF changes inherited query discrimination, entity assignability, or the
  metadata identity of a configured hierarchy owner.
- EF changes its temporary-value sidecar setter or the behavior of `PropertyEntry.IsTemporary`.
- A supported provider registers its own `IDatabase` implementation or EF changes when it calls that service.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.
- 2026-09-24: Managed insertions validate EF's final write set at the relational database service, allow ordinary callback writes, and restore their accepted state after rollback.
- 2026-09-26: D-012 removes the retained scope-wide compatibility surface without changing coordinated save ownership.
- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed coordinated asynchronous saves, ordering refresh, callback write recovery, and rollback regression specifications against the linked repository evidence.
- 2026-10-03: Coordinated saves were bound to the cached configured owner after reproducing mixed-subtype Parent resolution and base-order selector failures.
- 2026-10-03: Derived-only generated tokens were included in owner-level scalar refresh after a subsequent payload save reproduced stale rowversion state.
- 2026-11-01: All save paths preserve manual detection, including actual hierarchy coordination and managed insertion. Explicitly recorded work coordinates; unrecorded hierarchy work rejects. Final persistence and zero-write guards preserve callback safety without detecting unrelated payload.
- 2026-11-01: Review regressions aligned navigation observation with tracked removals and relevant FK effects, removed redundant detection-event guard scans, and added isolated conceptual-null and save-delegate lifetime checks plus real cascade-rejection coverage.
- 2026-11-01: Candidate and ordinary observer regressions exposed CLR key mutations hidden by generated sidecars. Added independent CLR authorization and shared the existing typed getter; positive divergent-storage controls and unchanged allocation budgets remain enforced.
- 2026-11-01: Navigation guards use the pre-suppression Parent target, independent relationship recovery retains pending graphs, and prepared-owner imports separate full-plan membership from active-batch persistence.
- 2026-11-02: Replaced late current-FK baselining with independent pre-command write evidence and authoritative generated readback. Procedure originals and unmarked temporary FKs cannot authorize saved membership; planned Parent nulling uses selected logical pairs without allocating another principal vector.
- 2026-11-02: Review regressions established the absent-old-principal exception to that pair comparison. Restored its narrow intended lookup, shared ordinary-FK proof across native save paths, and placed late callback-owner captures in the existing coordinated rollback snapshot before library membership changes.
- 2026-11-02: Mixed-member regressions disproved that late whole-owner capture. Replaced it with exact inverse additions, kept original pre-save membership authoritative, and repaired hidden installed FK identities without accepting their side effects on original values or modification flags.
- 2026-11-02: Extended pre-save endpoint evidence to initially unchanged FKs. Kept supplemental recovery selective so the repair does not erase unrelated pending CLR edits; verified original native absence, ordered duplicates, idempotence and unexpected-Parent cleanup. Avoided redundant equal original-key vectors.
- 2026-11-02: Recovered originally Unchanged sharing peers promoted without property flags before scalar replay. Native-only collection repair now follows receipt undo and retains independent unmarked CLR order. Added real OwnsOne/table-split faults, success/retry controls and complete FK resource probes through 20,000.
- 2026-11-02: Complex raw graph and managed insertion regressions refined sharing-peer recovery. Capture follows actual same-row command metadata and retains callback timing. Plain EF controls establish native JSON ordinal writes without DetectChanges; no library-wide detection or alternative JSON policy is introduced.
- 2026-11-02: Preserved post-capture complex topology through the D-004 journal, without changing coordinated versus managed callback timing. Added lifecycle, ordinal-identity, failure/retry and complete resource controls.
- 2026-11-03: Consolidated public recovery contracts, regression references and resource guidance in their maintained guides. Removed development-stage report dependencies; decision and runtime contracts are unchanged.

### Implementation References

- [Manual-detection and callback recovery contract](../configuration.md#manual-change-detection)
- [Coordinated-save ownership and persisted membership](../implementation-design.md#ordering-and-coordinated-saves)
- [Recovery regression inventory](../regression-coverage.md#manual-detection-and-hierarchy-guards)
- [Prepared-import and recovery costs](../performance.md#prepared-imports-and-recovery-costs)
- [Complex topology controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/ManagedSave/ComplexTopologyRollbackTests.cs)
- [Complex callback provider controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/SaveChanges/SaveChangesTests.ComplexCallbackRecovery.cs)
- [Managed callback sharing closure](../../src/Doka.EntityFrameworkCore.NestedSet/Features/ManagedSave/NestedSetSaveChanges.Sharing.cs)
- [Procedure/readback proof regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/ManagedSave/PersistedRelationshipProofTests.cs)
- [Save coordinator](../../src/Doka.EntityFrameworkCore.NestedSet/Features/ManagedSave/NestedSetSaveChanges.cs)
- [Persistence boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetRelationalDatabase.cs)
- [Managed save callback regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/SaveChanges/ManagedSaveCallbackTests.cs)
- [Managed hierarchy payload regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Insert/ManagedHierarchyPayloadTests.cs)
- [Mapped ordering](../../src/Doka.EntityFrameworkCore.NestedSet/Mapping/NestedSetOrdering.cs)
- [Ordering components](../../src/Doka.EntityFrameworkCore.NestedSet/Features/Ordering)
- [Ordering regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering)
- [Save regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/SaveChanges)
- [Manual-detection regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/SaveChanges/SaveChangesTests.ManualDetection.cs)
- [Read-only role and navigation regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/ManagedSave/HierarchyChangeDetectionTests.cs)
- [Mixed inheritance managed-save regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/ModelCompatibility/Inheritance/InheritanceTests.ManagedSave.cs)
- [Derived rowversion regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Ordering/Tracking/RowVersion/DerivedRowVersionTests.cs)
- [Subtype-aware scalar refresh](../../src/Doka.EntityFrameworkCore.NestedSet/Features/ManagedSave/NestedSetRefreshProperties.cs)
- [Tracker snapshot allocation regression](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackerSnapshotAllocationTests.cs)
- [Ordering contract](../../docs/ordering.md)

### Sources

- [EF complex detection and lane trimming](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/ChangeDetector.cs#L681-L810) (primary source; retrieved 2026-11-02)
- [EF complex collection acceptance and original-lane replacement](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.InternalComplexCollectionEntry.cs#L172-L249) (primary source; retrieved 2026-11-02)
- [EF tracked same-row traversal](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.Relational/Update/Internal/SharedTableEntryMap.cs#L76-L129) (primary source; retrieved 2026-11-02)
- [EF JSON ordinal creation](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.InternalComplexCollectionEntry.cs#L80-L110) (primary source; retrieved 2026-11-02)
- [EF JSON write selection](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.Relational/Update/ModificationCommand.cs#L738-L805) (primary source; retrieved 2026-11-02)
- [EF shared-row peer promotion](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.Relational/Update/Internal/CommandBatchPreparer.cs#L321-L352) (primary source; retrieved 2026-11-02)
- [EF provider state setter without property modification](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L117-L120) (primary source; retrieved 2026-11-02)
- [EF state-transition original rejection](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs#L283-L320) (primary source; retrieved 2026-11-02)
- [EF shared concurrency-token mapping](https://learn.microsoft.com/en-us/ef/core/modeling/table-splitting#concurrency-tokens) (primary source; retrieved 2026-11-02)
- [EF Core native collection snapshot removal](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.RelationshipsSnapshot.cs#L54-L60) (primary source; retrieved 2026-11-02)
- [EF Core native setter original-value handling](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs#L432-L454) (primary source; retrieved 2026-11-02)
- [EF Core provider execution before acceptance](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/StateManager.cs#L1426-L1436) (primary source; retrieved 2026-11-02)
- [EF Core completion interception and events](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/DbContext.cs#L778-L797) (primary source; retrieved 2026-11-02)
- [EF Core detached reference and conditional nulling](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/NavigationFixer.cs#L1369-L1438) (primary source; retrieved 2026-11-02)
- [EF Core public tracked-key lookup](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Update/IUpdateAdapter.cs#L63-L69) (primary source; retrieved 2026-11-02)
- [EF Core compatible principal mapping filter](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/StateManager.cs#L912-L953) (primary source; retrieved 2026-11-02)
- [EF Core 10.0.12 public update-entry readback contract](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Update/IUpdateEntry.cs) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.14 modified columns and procedure inputs](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.Relational/Update/ModificationCommand.cs) (primary source; retrieved 2026-11-01)
- [EF Core procedure model validation](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.Relational/Infrastructure/RelationalModelValidator.cs) (primary source; retrieved 2026-11-01)
- [EF Core generated principal-to-FK propagation](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs#L448-L467) (primary source; retrieved 2026-11-01)
- [EF Core FK original and store-generated storage](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Metadata/Internal/PropertyExtensions.cs#L137-L166) (primary source; retrieved 2026-11-01)
- [EF Core automatic and explicit detection](https://learn.microsoft.com/en-us/ef/core/change-tracking/change-detection) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.14 local detection and principal traversal](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/ChangeDetector.cs) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.14 navigation FK and lifecycle effects](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/ChangeTracking/Internal/NavigationFixer.cs) (primary source; retrieved 2026-11-01)
- [EF Core minimal-overlap FK selection](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Metadata/Internal/ForeignKeyExtensions.cs#L169-L209) (primary source; retrieved 2026-11-01)
- [EF Core 10.0.12 inherited query scope](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.CreateSelect.cs) (primary source; retrieved 2026-10-03)
- [EF Core SaveChanges](https://learn.microsoft.com/en-us/ef/core/saving/) (primary source; retrieved 2026-09-23)
- [EF Core save events](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/events) (primary source; retrieved 2026-09-23)
- [EF Core tracked entry state](https://learn.microsoft.com/en-us/ef/core/change-tracking/entity-entries) (primary source; retrieved 2026-09-23)
- [EF Core temporary values](https://learn.microsoft.com/en-us/ef/core/change-tracking/miscellaneous#temporary-values) (primary source; retrieved 2026-09-24)
- [EF Core PropertyEntry.IsTemporary](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/ChangeTracking/PropertyEntry.cs) (primary source; retrieved 2026-09-24)
- [EF Core temporary sidecar implementation](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs) (primary source; retrieved 2026-09-24)
- [EF Core DbContext save sequence](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/DbContext.cs) (primary source; retrieved 2026-09-24)
- [EF Core RelationalDatabase](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Storage/RelationalDatabase.cs) (primary source; retrieved 2026-09-24)
- [EF Core relational service registration](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Infrastructure/EntityFrameworkRelationalServicesBuilder.cs) (primary source; retrieved 2026-09-24)
