namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal static partial class SignalReportBuilder
{
    /// <summary>
    /// Constructs the complete deferred UNION ALL, path, owner and SUM expression without database access.
    /// </summary>
    internal static IQueryable<SignalReportTotal> Build(
        SignalLabContext context,
        ValidatedSignalReport request
    )
    {
        var tenant = request.TenantId;
        var anchors = request.AnchorIds;
        var channels = request.ChannelIds;
        var sources = request.Sources;
        var granularity = request.Granularity;
        var from = request.FromInclusive;
        var until = request.UntilExclusive;
        var tree = context
            .NestedSet<SignalNode>()
            .ForScope(tenant)
            .InTree(request.TreeId);

        // WHY: SQLite's expanded collection parameters disable EF's command-template cache. One JSON
        // parameter per selection keeps that template reusable. Other providers retain their cardinality policy.
        var singleCollectionParameter = context.Database.IsSqlite();
        var definitions = context.SignalChannels.Where(channel => channel.TenantId == tenant);
        definitions = singleCollectionParameter
            ? definitions.Where(channel => Enumerable.Contains(EF.Parameter(channels), channel.Id))
            : definitions.Where(channel => Enumerable.Contains(channels, channel.Id));

        var measurements = context
            .PulseRecords
            .Where(value => value.TenantId == tenant)
            .Select(s_pulseMeasurement)
            .Concat(
                context
                    .SweepRecords
                    .Where(value => value.TenantId == tenant)
                    .Select(s_sweepMeasurement))
            .Concat(
                context
                    .BurstRecords
                    .Where(value => value.TenantId == tenant)
                    .Select(s_burstMeasurement))
            .Where(value => value.Granularity == granularity && value.Day >= from && value.Day < until);

        measurements = singleCollectionParameter
            ? measurements.Where(value => Enumerable.Contains(EF.Parameter(channels), value.ChannelId)
                && Enumerable.Contains(EF.Parameter(sources), value.Source))
            : measurements.Where(value => Enumerable.Contains(channels, value.ChannelId)
                && Enumerable.Contains(sources, value.Source));

        var measurementDays = measurements
            .Select(ReportProjections.MeasurementDay)
            .Distinct();

        // WHY: A path depends on its anchor, measured node and day, not the channel or physical fact source.
        // Qualify that unique grain first, then restore every physical fact below. Starting from measured
        // nodes still avoids expanding a broad anchor's entire subtree before finding its sparse facts.
        var endpoints = tree.SelectAncestorsOrSelf(
            node => measurements.Any(measurement => measurement.TenantId == node.TenantId
                && measurement.NodeId == node.Id),
            ReportProjections.Endpoints);

        endpoints = singleCollectionParameter
            ? endpoints.Where(pair => Enumerable.Contains(EF.Parameter(anchors), pair.Anchor.Id))
            : endpoints.Where(pair => Enumerable.Contains(anchors, pair.Anchor.Id));

        var paths = endpoints.Join(
            measurementDays,
            pair => new
            {
                pair.Node.TenantId,
                NodeId = pair.Node.Id,
            },
            day => new
            {
                day.TenantId,
                day.NodeId,
            },
            ReportProjections.Path);

        foreach (var group in request.Groups)
        {
            var type = group.TypeId;
            var predicate = OwnerPredicate<ReportPath>(context, tenant, group);
            paths = group.Mode == TagOwnerMode.Any
                ? tree.WhereAnyAncestorOrSelf(paths, ReportProjections.Node, predicate)
                : tree.WhereNearestAncestorOrSelf(
                    paths,
                    ReportProjections.Node,
                    NearestOwnerCandidate(type),
                    predicate);
        }

        // WHY: These pure metadata and temporal filters only reject the same Anchor/Node/Day row. Owners
        // are selected from the unchanged tree, so applying metadata first preserves nearest/no-fallback.
        // Keeping the large temporal expression last avoids revisiting it in every metadata provenance check.
        paths = tree.WherePathEdges(
            paths,
            ReportProjections.Anchor,
            ReportProjections.Node,
            AllowedPeriods(context, tenant));

        // WHY: Keep the qualified relation scalar and unique before rejoining facts. This boundary prevents
        // a merged join from repeating an expensive day-specific proof for every channel and source. It never
        // deduplicates measurements or projected values, and each overlapping anchor retains its own identity.
        var qualified = paths
            .Select(ReportProjections.QualifiedPath)
            .Distinct();

        var contributions = qualified
            .Join(
                measurements,
                path => new
                {
                    path.TenantId,
                    path.NodeId,
                    path.Day,
                },
                measurement => new
                {
                    measurement.TenantId,
                    measurement.NodeId,
                    measurement.Day,
                },
                ReportProjections.Measurement)
            .Join(
                definitions,
                row => new
                {
                    row.Measurement.TenantId,
                    Id = row.Measurement.ChannelId,
                },
                definition => new
                {
                    definition.TenantId,
                    definition.Id,
                },
                ReportProjections.Definition)
            .Where(ReportProjections.MatchingMeasurementType);

        return contributions
            .GroupBy(row => new
            {
                row.AnchorId,
                row.Measurement.ChannelId,
                row.Measurement.Source,
            })
            .Select(group => new SignalReportTotal
            {
                AnchorId = group.Key.AnchorId,
                ChannelId = group.Key.ChannelId,
                Source = group.Key.Source,
                Value = group.Sum(row => row.Measurement.Value),
            });
    }
}
