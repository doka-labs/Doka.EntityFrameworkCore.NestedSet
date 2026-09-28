# Diagnostics and observability

NestedSet exposes typed failures plus standard .NET `ActivitySource` and
`Meter` signals. It adds no OpenTelemetry package, logging framework, or
exporter dependency.

## Sources

Both public names are available through `NestedSetDiagnostics`:

```csharp
NestedSetDiagnostics.ActivitySourceName
NestedSetDiagnostics.MeterName
```

Their value is `Doka.EntityFrameworkCore.NestedSet`. Subscribe with standard
.NET diagnostics or add that source and meter to the application's
OpenTelemetry pipeline.

## Metrics

| Instrument | Type | Unit | Meaning |
| --- | --- | --- | --- |
| `nestedset.operation.duration` | `Histogram<double>` | seconds | Complete public operation, including transaction completion and cleanup |
| `nestedset.operation.count` | `Counter<long>` | operations | Completed operations by operation and outcome |
| `nestedset.operation.failures` | `Counter<long>` | failures | Failed operations, excluding cancellation |
| `nestedset.lock.wait.duration` | `Histogram<double>` | seconds | Provider lock acquisition, including failed or canceled waits |
| `nestedset.rows.affected` | `Histogram<long>` | rows | Sum of hierarchy rows reported by database writes in one operation |
| `nestedset.batch.count` | `Histogram<long>` | batches | Bounded write batches used by one bulk or rebuild operation |
| `nestedset.rebuild.node.count` | `Histogram<long>` | nodes | Nodes inspected by one rebuild or rebuild plan |

The duration histograms publish fixed bucket advice from submillisecond work
through 60 seconds. A collector may replace that view. The advice is an
aggregation aid, not a service-level objective.

Rows affected counts command-reported hierarchy row writes. A row updated by
several structural statements contributes to each statement, so this signal is
write amplification rather than a distinct-node count. Registry lock writes and
application payload rows are excluded. A failed transaction can report
attempted rows that were later rolled back.

## Activities and tags

Public mutations, coordinated saves, validation, rebuild planning, and rebuild
create internal activities named `nestedset.<operation>`. Lock acquisition
creates `nestedset.lock`. Deferred query builders execute through EF Core and
use EF's normal diagnostics.

Signals use only bounded library values:

| Tag | Values |
| --- | --- |
| `nestedset.operation` | Library-owned operation vocabulary |
| `nestedset.outcome` | `success`, `failure`, or `canceled` |
| `nestedset.provider` | `mysql_mariadb`, `postgresql`, `sqlite`, `sql_server`, or `unknown` |
| `error.type` | Bounded failure classification when an exception occurred |

The package never emits NodeKey, Scope, TreeId, entity or table names, SQL,
connection strings, exception messages, stack traces, or domain payload as
telemetry. This avoids sensitive data disclosure and unbounded metric
cardinality.

## Typed failures

Use `NestedSetException.Code` for application decisions. Do not parse exception
messages. Provider exceptions remain available when a library wrapper would
remove useful database evidence.

`ValidateAsync` returns node keys only in its caller-owned
`NestedSetValidationReport`. Reports are application data and remain separate
from process-wide telemetry.

## Correlation

NestedSet activities inherit the current `Activity`:

```csharp
using var activity = applicationActivitySource.StartActivity("group.move");
await groups.MoveToAsync(groupId, parentId, cancellationToken);
```

Attach approved correlation data to the application span or a protected audit
record. Do not add tenant IDs, user IDs, raw keys, role names, or folder paths
to metric tags.

## Operational interpretation

| Signal | First questions |
| --- | --- |
| Increased lock wait | Did transaction duration rise, or did writer concentration move to one TreeId? |
| Increased duration with normal lock wait | Did subtree width, write amplification, index shape, or query plan change? |
| Increased rows affected | Did tree size or mutation position increase the expected nested-set range shifts? |
| Increased batch count | Did bulk/rebuild input grow, or did a release change the documented batch size? |
| `invalid_structure` | Did direct SQL, a partial migration, or an external writer bypass the protocol? |
| `invalid_transaction` | Is the transaction outside the execution-strategy delegate or otherwise incompatible? |
| `tree_id_unavailable` | Is the requested identity active or intentionally preserved as a tombstone? |
| `tree_id_not_tombstoned` | Did an administrative purge select an active identity? |
| `database` | Inspect provider and database evidence; the bounded category is not a root cause. |

Alert thresholds must come from the deployed workload. Useful alerts combine
rate and duration, such as sustained non-cancellation failures, lock-wait
percentiles with saturation, or any new structural validation issue. GitHub
runner timing is not an acceptance baseline; see [Performance](performance.md).
