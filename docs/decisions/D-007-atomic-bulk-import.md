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

### Re-evaluation Triggers

- A measured ingestion workload requires streaming beyond the complete-input memory contract.
- A provider-native path can preserve the same generated-value and rollback behavior with independently qualified evidence.
- EF changes insertion callbacks or generated-key propagation used by the staged import.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed atomic forest and branch import, bounded structural batches, and rejection and rollback regression specifications against the linked repository evidence.

### Implementation References

- [Bulk coordinator](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetBulkInsert.cs)
- [Bulk plan](../../src/Doka.EntityFrameworkCore.NestedSet/Features/BulkImport/NestedSetBulkPlan.cs)
- [Mapped storage batches](../../src/Doka.EntityFrameworkCore.NestedSet/Storage)
- [Bulk contract tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/BulkImport)
- [Interval refresh regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/BulkImport/BulkIntervalRefreshTests.cs)
- [Import guide](../../docs/bulk-import.md)

### Sources

- No external sources; repository evidence only.
