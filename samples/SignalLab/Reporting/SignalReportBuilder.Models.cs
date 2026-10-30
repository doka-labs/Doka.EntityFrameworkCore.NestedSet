namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportBuilder
{
    /// <summary>Retains mapped endpoints only until one scalar day-specific path decision is qualified.</summary>
    private sealed class ReportPath
    {
        /// <summary>Gets the visible requested anchor whose incoming edge is excluded from its own path.</summary>
        public required SignalNode Anchor { get; init; }

        /// <summary>Gets the visible node that owns one or more independent physical facts.</summary>
        public required SignalNode Node { get; init; }

        /// <summary>Gets the common measurement day used to qualify every exact direct edge.</summary>
        public DateOnly Day { get; init; }
    }

    /// <summary>Provides identical scalar shapes for the three independent physical fact sources.</summary>
    internal sealed class MeasurementRow
    {
        /// <summary>Gets the tenant that owns this fact.</summary>
        public Guid TenantId { get; init; }

        /// <summary>Gets the measurement node identity.</summary>
        public Guid NodeId { get; init; }

        /// <summary>Gets the additive channel identity.</summary>
        public Guid ChannelId { get; init; }

        /// <summary>Gets the explicitly selected measurement grain.</summary>
        public MeasurementGranularity Granularity { get; init; }

        /// <summary>Gets the application measurement day.</summary>
        public DateOnly Day { get; init; }

        /// <summary>Gets the additive scalar measurement.</summary>
        public double Value { get; init; }

        /// <summary>Gets the independent physical source.</summary>
        public CapturePort Source { get; init; }
    }

    /// <summary>Keeps entity aliases only inside the deferred query; final projections contain scalars.</summary>
    internal sealed class Contribution
    {
        /// <summary>Gets the mapped anchor alias without materializing it.</summary>
        public required SignalNode Anchor { get; init; }

        /// <summary>Gets the mapped measurement owner alias.</summary>
        public required SignalNode Node { get; init; }

        /// <summary>Gets the deferred scalar fact projection.</summary>
        public required MeasurementRow Measurement { get; init; }

        /// <summary>Gets the channel classification used to exclude stored intermediate aggregates.</summary>
        public Guid MeasurementTypeId { get; init; }
    }
}
