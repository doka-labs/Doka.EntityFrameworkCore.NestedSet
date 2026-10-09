---
id: D-007
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Bulk insert atomicity, generated keys, payload compatibility, and write amplification"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-007 -- Import a complete branch through EF with bounded structural batches

## Context and Problem Statement

Repeatedly inserting each imported node opens and closes structural gaps many times. At the same time, application
payload, generated identities, and mapped values need EF behavior. The existing bulk path plans a complete input,
reserves one interval, saves the entity wave, and resolves derived structure inside the shared mutation boundary.

## Decision Drivers

- Avoid one complete structural mutation per imported node.
- Preserve generated keys and mapped payload through EF insertion.
- Reject foreign destinations, inconsistent input graphs, and unsaved stage mutations.
- Retain all-or-nothing rollback and explicit O(n) import-state cost.

## Considered Options

- One EF insertion wave with bounded structural batches
- Repeated public single-node insertion
- Provider-native bulk copy for complete rows

## Decision Outcome

Chosen option: "One EF insertion wave with bounded structural batches", because it reuses the ordinary transaction
and storage foundations while preserving EF payload semantics across the supported providers.

### Consequences

- Good, because one planned import can reduce structural write amplification while including generated-key resolution and ordering atomically.
- Bad, because it is not a streaming constant-memory loader or a guarantee of provider-native bulk-copy throughput.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx -c Release --filter "FullyQualifiedName~Bulk"` and expect all bulk regression cases to pass.
- Require generated keys, assigned keys, foreign parent/scope rejection, repeated input references, callback stage corruption, cancellation, and interval-refresh failure coverage. Expect atomic rollback and the configured write-budget assertions to hold.
- Run the real relational capacity cases separately on all five engines. One
  million direct children must pass through the public import, and million-node
  failure/cancellation at final refresh must restore every earlier payload wave,
  caller input, and reservation. Record sampled additional occupied heap and
  allocation traffic with provider/framework overhead included, excluding only
  pre-existing caller input; identify key types and original input state.

## Pros and Cons of the Options

### One EF insertion wave with bounded structural batches

- Good, because EF assigns values while shared planning reduces repeated interval shifts and parent refresh work.
- Bad, because import state and staged entities remain in memory, and callbacks require explicit stage guards.

### Repeated public single-node insertion

- Good, because applications can reuse the simple insertion API without another import contract.
- Bad, because many nodes cause repeated locks, interval updates, and save operations unless the caller manually coordinates them.

### Provider-native bulk copy for complete rows

- Good, because large provider-specific imports could achieve higher raw insert throughput.
- Bad, because generated keys, converters, application callbacks, and transaction semantics would need separate implementations for each provider.

## More Information

The input plan validates the branch before structural writes and guards the saved stage afterward. Bounded
repair/refresh batches share infrastructure with ordinary maintenance. The operation does not reinterpret
application authorization or make unrelated pending changes safe to flush.

Final refresh applies mapped CLR values through EF's compiled property setters,
without creating an EntityEntry for each detached input. EF retains newly
created detached entries in its state manager; setting Detached again does not
remove that initial registration. The million-node qualification and a
weak-reference regression exposed this retention despite an empty visible
ChangeTracker. Clearing the caller's tracker or temporarily retracking inputs
would change application state and callbacks, so neither is used.

The setter is a public extension interface, but acquiring it requires EF's
internal runtime-property contract. The framework binding uses reflection once
at registration. Its compiled delegate then acquires the metadata-owned setter
once per weakly cached property, outside the row loop; applying a value uses the
public compiled setter. This retains EF's field/indexer/proxy access and nested
value-complex copyback rather than duplicating its setter generator. The shared
framework binding contract validates this setter seam at `UseNestedSets`
registration, before hierarchy writes. Runtime mapped-setter failures still
follow the existing rollback boundary.
Shadow properties have no detached CLR representation to refresh; persisted
shadow values remain available through normal queries.

Identity refresh retains its capture, end-of-staging, and provider-completion
boundaries. Cached typed comparisons reuse unchanged scalar key snapshots
before boxing or creating object vectors. Generated and temporary sidecars,
conceptual nulls, mapped CLR access, and exact key comparers remain part of
that comparison. Mutable reference keys still take independent snapshots and
protect their native map slots at each boundary. Foreign-key vectors are
bounded and reused, but every boundary still captures newly created dependent
buckets. Skipping that check when FK values are unchanged would miss
collections materialized by EF after the preceding observation.

Non-sentinel structural snapshots retain coordinates as validated native
`long`/`int` fields, with a bitmap distinguishing captured zero from an omitted
sentinel. Heterogeneous nullable identity representations retain metadata
snapshots. This removes persistent coordinate boxing without weakening exact
rollback or imposing a new model representation on callers.

Generated input values are captured before their insertion batch changes them.
A separate capture flag distinguishes an all-sentinel snapshot from an input
not yet staged; early failure leaves the latter's generated payload untouched.
Assigned NodeKeys are checked against their initial independent snapshots
before and after payload persistence. Only genuinely generated identities can
replace the plan's initial key representation.

Staging finishes generated CLR reads and structural CLR writes before obtaining
an EF entry. An exception in either access path otherwise leaves the initially
detached input registered in the context's reference map. Shadow assignments
still use the exact entry before normal graph tracking. This preserves EF's
tracking callbacks while allowing a failed input to be collected independently
of a caller-owned context and its unrelated tracked entities.

The remaining lifecycle recovery is shared with single insertion. Each active
batch owns its roots and owned dependents before tracking transitions, captures
exact installed identities, and refreshes generated identities after provider
persistence. Mutable map keys use independent comparer snapshots. Normal
detachment is followed by exact surviving native membership cleanup when a
rejected transition or callback mutation prevents EF's current-key cleanup.
The helper does not retain lifecycle handles for earlier detached batches.
Completed batches retain only insertion-owned CLR values that can require
rollback: generated leaves, owned keys, and ownership foreign keys captured
before relationship fixup. Ordinary payload and business foreign keys are
excluded. This state grows with the application's owned model; a plain root
without such owned values does not allocate an owned-value snapshot.
Its infrastructure contracts and the reason public detachment is insufficient
are recorded in D-004. Forest and subtree insertion both register the same
provider-result callback; generated subtree keys must not be mistaken for
unauthorized callback edits.

### Re-evaluation Triggers

- A measured ingestion workload requires streaming beyond the complete-input memory contract.
- A provider-native path can preserve the same generated-value and rollback behavior with independently qualified evidence.
- EF changes insertion callbacks or generated-key propagation used by the staged import.
- A real-size import exceeds its stated additional-heap budget, including
  non-sentinel structural rollback values; profile retained state before
  changing snapshots, batch size, or callback guards.
- EF changes its runtime-property setter acquisition contract or complex-value
  copyback behavior. Requalify metadata access, compiled models, generated-value
  refresh/rollback, and detached-input collection before upgrading.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed atomic forest and branch import, bounded structural batches, and rejection and rollback regression specifications against the linked repository evidence.
- 2026-10-03: Added real persisted capacity and late million-node rollback qualification instead of treating small-plan extrapolation as full execution evidence. Heap observations explicitly distinguish sampled maxima from exact peaks and retain non-sentinel caller structure.
- 2026-10-03: The real public import exposed retained detached EF entries during final refresh. Replaced entry creation with metadata-cached compiled setters while preserving complete mapped CLR assignment and caller tracker ownership.
- 2026-10-03: Million-node profiling identified persistent coordinate boxing in non-sentinel rollback snapshots. Retained native coordinate values and added non-sentinel allocation and sentinel-distinction controls.
- 2026-10-03: Guarded assigned-key callback edits, preserved never-staged generated inputs, and completed throwing CLR access before entry creation. Added exact restoration and weak-reference regressions for early failures.
- 2026-10-04: Shared exact insertion identity and partial tracking recovery with single insertion. Both forest and subtree saves capture generated results before acceptance; mutable root and owned identities retain bounded independent cleanup handles.
- 2026-10-04: Removed unchanged scalar identity-refresh vectors and repeated FK LINQ allocation while preserving all lifecycle observations and mutable-key protection. Moved insertion/setter framework compatibility checks to registration.

### Implementation References

- [Shared insertion-owned CLR snapshots](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionValues.cs)
- [Bulk coordinator](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetBulkInsert.cs)
- [Bulk plan](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetBulkPlan.cs)
- [Mapped storage batches](../../src/Doka.EntityFrameworkCore.NestedSet/Storage)
- [Bulk contract tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/BulkImport)
- [Detached CLR setter](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetDetachedValueSetter.cs)
- [Shared insertion lifecycle](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionTracking.cs)
- [Insertion framework binding contract](../../src/Doka.EntityFrameworkCore.NestedSet/Execution/NestedSetInsertionContract.cs)
- [Insertion recovery decision](../../docs/decisions/D-004-atomic-mutations-and-locks.md)
- [Installed identity recovery regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkIdentityRetentionTests.cs)
- [Detached input retention regression](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkDetachedRefreshTests.cs)
- [Early staging retention regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkStagingRetentionTests.cs)
- [Owned generated values and relationship fixup rollback](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/BulkImport/BulkOwnedGeneratedTests.cs)
- [Assigned-key callback regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/BulkImport/BulkStageGuardTests.AssignedKeys.cs)
- [Mapped setter shape regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/DetachedRefreshValueTests.cs)
- [Compact coordinate snapshot regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/BulkImport/BulkOriginalStructureTests.cs)
- [Interval refresh regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/BulkImport/BulkIntervalRefreshTests.cs)
- [Real relational capacity and rollback cases](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Capacity/CapacityTests.cs)
- [Heap and capacity qualification boundaries](../../docs/performance.md#real-relational-capacity-cases)
- [Import guide](../../docs/bulk-import.md)

### Sources

- [EF Core 10.0.12 identity-map lifecycle](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/IdentityMap.cs) (primary source; retrieved 2026-10-03)
- [EF Core 10.0.12 typed and conceptual-null current-value access](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/InternalEntryBase.cs) (primary source; retrieved 2026-10-04)
- [EF Core 10.0.12 temporary and generated typed property access](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/PropertyAccessorsFactory.cs) (primary source; retrieved 2026-10-04)
- [EF Core value comparers and key snapshots](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers) (primary source; retrieved 2026-10-03)
- [EF Core 10.0.12 detached-reference map](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/EntityReferenceMap.cs) (primary source; retrieved 2026-10-03)
- [EF Core 10.0.12 runtime-property setter contract](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/IRuntimePropertyBase.cs) (primary source; retrieved 2026-10-03)
- [EF Core CLR setter extension interface](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.metadata.iclrpropertysetter?view=efcore-10.0) (primary source; retrieved 2026-10-03)
- [EF Core 10.0.12 CLR setter generator](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Internal/ClrPropertySetterFactory.cs) (primary source; retrieved 2026-10-03)
