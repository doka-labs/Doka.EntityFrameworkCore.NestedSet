using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportBuilder
{
    /// <summary>Owns reusable expression graphs with no context, model, request or query root.</summary>
    private static class ReportProjections
    {
        // WHY: These shapes depend only on sample DTO members. Reusing their immutable graphs avoids
        // repeated lambda/member discovery while every query root and request predicate stays privately bound.

        /// <summary>Retains the scalar grain that shares one day-specific path decision across facts.</summary>
        internal static readonly Expression<Func<MeasurementRow, MeasurementDay>> MeasurementDay = value =>
            new MeasurementDay
            {
                TenantId = value.TenantId,
                NodeId = value.NodeId,
                Day = value.Day,
            };

        /// <summary>Retains mapped endpoint aliases before adding the scalar measurement day.</summary>
        internal static readonly Expression<Func<SignalNode, SignalNode, int, ReportEndpoints>> Endpoints = (
            node,
            anchor,
            distance
        ) => new ReportEndpoints
        {
            Anchor = anchor,
            Node = node,
        };

        /// <summary>Binds one endpoint pair to one distinct measurement day without copying a physical fact.</summary>
        internal static readonly Expression<Func<ReportEndpoints, MeasurementDay, ReportPath>> Path = (
            pair,
            day
        ) => new ReportPath
        {
            Anchor = pair.Anchor,
            Node = pair.Node,
            Day = day.Day,
        };

        /// <summary>Selects the mapped lower endpoint for metadata and exact direct-path decisions.</summary>
        internal static readonly Expression<Func<ReportPath, SignalNode>> Node = row => row.Node;

        /// <summary>Selects the mapped upper endpoint whose incoming edge is excluded from its own path.</summary>
        internal static readonly Expression<Func<ReportPath, SignalNode>> Anchor = row => row.Anchor;

        /// <summary>Removes mapped entities before the distinct day-specific path boundary.</summary>
        internal static readonly Expression<Func<ReportPath, QualifiedReportPath>> QualifiedPath = row => new()
        {
            AnchorId = row.Anchor.Id,
            TenantId = row.Node.TenantId,
            NodeId = row.Node.Id,
            NodeTypeId = row.Node.TypeId,
            Day = row.Day,
        };

        /// <summary>Restores every physical fact after the unique scalar path has been qualified.</summary>
        internal static readonly Expression<Func<QualifiedReportPath, MeasurementRow, MeasuredContribution>>
            Measurement = (
                path,
                measurement
            ) => new MeasuredContribution
            {
                AnchorId = path.AnchorId,
                NodeTypeId = path.NodeTypeId,
                Measurement = measurement,
            };

        /// <summary>Adds only the channel classification needed to exclude stored intermediate aggregates.</summary>
        internal static readonly Expression<Func<MeasuredContribution, SignalChannel, DefinedContribution>> Definition =
        (
            row,
            definition
        ) => new DefinedContribution
        {
            AnchorId = row.AnchorId,
            NodeTypeId = row.NodeTypeId,
            Measurement = row.Measurement,
            MeasurementTypeId = definition.MeasurementTypeId,
        };

        /// <summary>Limits additive measurements to their explicitly configured hierarchy classification.</summary>
        internal static readonly Expression<Func<DefinedContribution, bool>> MatchingMeasurementType =
            row => row.NodeTypeId == row.MeasurementTypeId;
    }

    /// <summary>Identifies one tenant/node/day independently of channel, value and physical source.</summary>
    private sealed class MeasurementDay
    {
        /// <summary>Gets the tenant of the measurement owner.</summary>
        public Guid TenantId { get; init; }

        /// <summary>Gets the measurement owner identity.</summary>
        public Guid NodeId { get; init; }

        /// <summary>Gets the day on which every direct edge must be valid.</summary>
        public DateOnly Day { get; init; }
    }

    /// <summary>Keeps mapped endpoint aliases only before scalar path qualification.</summary>
    private sealed class ReportEndpoints
    {
        /// <summary>Gets the requested visible anchor alias.</summary>
        public required SignalNode Anchor { get; init; }

        /// <summary>Gets the visible measurement owner alias.</summary>
        public required SignalNode Node { get; init; }
    }

    /// <summary>Defines the complete scalar DISTINCT boundary without entities or physical fact values.</summary>
    private sealed class QualifiedReportPath
    {
        /// <summary>Gets the anchor identity retained separately for overlapping requested subtrees.</summary>
        public Guid AnchorId { get; init; }

        /// <summary>Gets the tenant that isolates the measurement owner.</summary>
        public Guid TenantId { get; init; }

        /// <summary>Gets the measurement owner identity.</summary>
        public Guid NodeId { get; init; }

        /// <summary>Gets the measurement owner's hierarchy classification.</summary>
        public Guid NodeTypeId { get; init; }

        /// <summary>Gets the day whose exact path has been qualified.</summary>
        public DateOnly Day { get; init; }
    }

    /// <summary>Restores the original fact bag without retaining mapped hierarchy entities.</summary>
    private sealed class MeasuredContribution
    {
        /// <summary>Gets the independently requested anchor identity.</summary>
        public Guid AnchorId { get; init; }

        /// <summary>Gets the measurement owner's hierarchy classification.</summary>
        public Guid NodeTypeId { get; init; }

        /// <summary>Gets one original physical measurement projection, including duplicate values.</summary>
        public required MeasurementRow Measurement { get; init; }
    }

    /// <summary>Adds the definition classification before summing each anchor, channel and source.</summary>
    private sealed class DefinedContribution
    {
        /// <summary>Gets the independently requested anchor identity.</summary>
        public Guid AnchorId { get; init; }

        /// <summary>Gets the measurement owner's hierarchy classification.</summary>
        public Guid NodeTypeId { get; init; }

        /// <summary>Gets one original physical measurement projection.</summary>
        public required MeasurementRow Measurement { get; init; }

        /// <summary>Gets the only hierarchy classification to which this channel is additive.</summary>
        public Guid MeasurementTypeId { get; init; }
    }
}
