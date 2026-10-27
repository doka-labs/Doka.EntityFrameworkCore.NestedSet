using Microsoft.Extensions.Logging;

namespace Doka.EntityFrameworkCore.NestedSet.Diagnostics;

/// <summary>Reports requested performance fallbacks through EF without retaining contexts or domain data.</summary>
internal static class NestedSetQueryDiagnostics
{
    private static readonly EventId s_ancestorIndexWithoutReader = new(
        70_004,
        "Doka.EntityFrameworkCore.NestedSet.Query.AncestorIndexWithoutReader");

    private static readonly ConditionalWeakTable<DbContext, ReportedFeature> s_reported = new();

    /// <summary>Reports a requested ancestor-index read on an adapter with schema support only.</summary>
    internal static void AncestorIndexWithoutReader(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = s_reported.GetValue(context, static _ => new ReportedFeature());

        if (!state.TryMark())
        {
            return;
        }

        Emit(context);
    }

    /// <summary>Constructs the first informational event outside the allocation-free repeated-query path.</summary>
    private static void Emit(
        DbContext context
    )
    {
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();

        // WHY: Schema-only support is informational. EF applies ConfigureWarnings to the effective level
        // before creating the constant-template delegate, preserving ILogger, LogTo and DiagnosticSource.
        var definition = new EventDefinition(
            logger.Options,
            s_ancestorIndexWithoutReader,
            LogLevel.Information,
            s_ancestorIndexWithoutReader.Name!,
            static level => LoggerMessage.Define(
                level,
                s_ancestorIndexWithoutReader,
                "HasAncestorIndex is configured, but this provider has no optimized NestedSet ancestor reader. "
                + "The schema remains configured and this read uses the portable query strategy."));

        if (logger.ShouldLog(definition))
        {
            definition.Log(logger);
        }

        if (logger.NeedsEventData(definition, out var diagnosticSource, out var simpleLog))
        {
            var data = new EventData(
                definition,
                static (eventDefinition, _) => ((EventDefinition)eventDefinition).GenerateMessage());

            logger.DispatchEventData(definition, data, diagnosticSource, simpleLog);
        }
    }

    /// <summary>Contains only the once-marker, never its weak context key or any scoped services.</summary>
    private sealed class ReportedFeature
    {
        private int _reported;

        /// <summary>Claims first reporting without locking or allocating on repeated query construction.</summary>
        internal bool TryMark()
        {
            if (Volatile.Read(ref _reported) != 0)
            {
                return false;
            }

            return Interlocked.Exchange(ref _reported, 1) == 0;
        }
    }
}
