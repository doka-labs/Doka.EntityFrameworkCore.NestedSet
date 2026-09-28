---
id: D-009
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Diagnostic dimensions, typed errors, authorization, and DbContext ownership"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-009 -- Expose bounded diagnostics while applications own access and context lifetime

## Context and Problem Statement

Operators need operation outcome, failure class, lock wait, write amplification,
batch size, and rebuild scale. Scope, TreeId, NodeKey, entity names, SQL, and
payload may identify tenants or people and would create unbounded telemetry
cardinality. Hierarchy work must use the application's existing context and
transaction rather than a hidden DI-owned unit of work.

## Decision Drivers

- Expose stable operational signals without sensitive or high-cardinality data.
- Preserve typed failures without parsing human-readable messages.
- Keep disabled instrumentation cheap on normal mutation paths.
- Preserve explicit application ownership of authorization, context disposal, and commit.
- Reuse standard .NET diagnostics without a new runtime dependency.

## Considered Options

- Bounded .NET diagnostics around the caller-owned context facade
- Include Scope, TreeId, NodeKey, entity, and exception text in signals
- Library-managed contexts, service factories, and authorization callbacks

## Decision Outcome

Chosen option: "Bounded .NET diagnostics around the caller-owned context
facade", because operation, outcome, provider family, and stable failure class
support aggregation without exposing application identity. Numeric work signals
report rows affected, batch count, and rebuild node count. Deferred query
execution remains visible through EF Core diagnostics.

### Consequences

- Good, because standard listeners can observe success, failure, cancellation, lock wait, provider family, write amplification, and maintenance scale.
- Good, because applications can correlate a library span with protected audit data without copying identifiers into shared metrics.
- Bad, because library telemetry alone cannot identify the affected business tree or explain a provider query plan.
- Bad, because applications must define their own alert thresholds and audit retention.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx --filter "FullyQualifiedName~DiagnosticsTests|FullyQualifiedName~ErrorContractTests|FullyQualifiedName~HistogramAdviceTests"` and expect exact bounded instrument names and tags, provider and row measurements, rebuild nodes and batches, and cancellation behavior to pass.
- Run `dotnet test Doka.EntityFrameworkCore.NestedSet.slnx --filter "FullyQualifiedName~TrackerSnapshotAllocationTests"` and expect the disabled instrumentation path to stay within its allocation budget.
- Captured activities and measurements must reject NodeKey, Scope, TreeId, entity or table names, SQL, connection data, exception messages, and payload.

## Pros and Cons of the Options

### Bounded .NET diagnostics around the caller-owned context facade

- Good, because observability composes with existing .NET and OpenTelemetry pipelines without another package.
- Bad, because detailed incident correlation remains an application responsibility.

### Include Scope, TreeId, NodeKey, entity, and exception text in signals

- Good, because a single event could name the affected business hierarchy.
- Bad, because identity values create unbounded dimensions and may disclose tenant, user, group, or path data.

### Library-managed contexts, service factories, and authorization callbacks

- Good, because some consumers could resolve one self-contained hierarchy service.
- Bad, because a hidden context cannot automatically join arbitrary domain writes, retries, interceptors, authorization, or caller commit ownership.

## More Information

`context.NestedSet<TEntity>()` is a lightweight facade over the existing
context. It is not a service-locator or authorization boundary. Validation
reports return node keys directly to the authorized caller and never export
them as tags. Rows affected is command-level write amplification; one row can
contribute more than once when several structural statements update it.
Full validation reports and rebuild plans retain at most 1,024 individual
issues, expose complete counts by stable code, and identify a truncated sample.
The older detailed service method retains its contract to return every node
issue and is documented as an explicit unbounded diagnostic operation.

### Re-evaluation Triggers

- A new diagnostic dimension can grow with application data or expose a sensitive value.
- An incident cannot be investigated with bounded library signals plus EF diagnostics and application correlation.
- A proposed lifetime change creates, retains, or disposes a context outside application ownership.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: The proposal documented bounded outcome and lock metrics plus an optional service factory.
- 2026-09-23: The caller-context facade, removal of the public service factory, and bounded provider, row, batch, and rebuild measurements were confirmed.
- 2026-09-23: Status changed from proposed to accepted.
- 2026-09-24: Full tree reports and rebuild plans gained bounded issue samples with complete typed counts; the detailed service contract remains unchanged.

- 2026-09-28: The maintainer confirmed acceptance and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed bounded telemetry, the caller-owned context facade, and diagnostics and allocation regression specifications against the linked repository evidence.

### Implementation References

- [Diagnostic names](../../src/Doka.EntityFrameworkCore.NestedSet/Diagnostics/NestedSetDiagnostics.cs)
- [Telemetry implementation](../../src/Doka.EntityFrameworkCore.NestedSet/Diagnostics/NestedSetTelemetry.cs)
- [Context facade](../../src/Doka.EntityFrameworkCore.NestedSet/NestedSetDbContextExtensions.cs)
- [Diagnostics tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Diagnostics)
- [Allocation tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution/TrackerSnapshotAllocationTests.cs)
- [Diagnostics guide](../../docs/diagnostics.md)
- [Security design](../security/security-design.md)

### Sources

- [.NET metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) (primary source; retrieved 2026-09-23)
- [.NET distributed tracing instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs) (primary source; retrieved 2026-09-23)
- [OpenTelemetry attribute requirement levels](https://opentelemetry.io/docs/specs/semconv/general/attribute-requirement-level/) (primary source; retrieved 2026-09-23)
