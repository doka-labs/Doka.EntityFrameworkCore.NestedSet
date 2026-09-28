namespace Doka.EntityFrameworkCore.NestedSet;

/// <summary>Identifies the built-in tracing and metrics instruments for hierarchy operations.</summary>
/// <remarks>
///     Subscribe with ActivityListener, MeterListener, or an OpenTelemetry collector. Tags contain only bounded
///     operation, outcome, and error classifications. Node keys, forest scopes, entity names, SQL, exception messages,
///     and domain payloads are never emitted. Deferred IQueryable execution is observed through EF's own diagnostics.
/// </remarks>
public static class NestedSetDiagnostics
{
    // WHY: A fixed query tag lets provider tests and database operators distinguish the final scalar refresh from
    // structurally identical placement reads without including entity names, keys, scopes, or other payload values.
    internal const string BulkRefreshTag = "Doka.EntityFrameworkCore.NestedSet.BulkRefresh";

    /// <summary>The ActivitySource name used for completed operations and lock acquisition.</summary>
    public const string ActivitySourceName = "Doka.EntityFrameworkCore.NestedSet";

    /// <summary>The Meter name used for operation counts, failures, duration, and lock wait duration.</summary>
    public const string MeterName = "Doka.EntityFrameworkCore.NestedSet";
}
