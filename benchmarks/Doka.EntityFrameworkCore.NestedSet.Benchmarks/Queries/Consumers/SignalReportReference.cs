using System.Linq.Expressions;
using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;
using Contribution = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.SignalReportBuilder.Contribution;
using MeasurementRow = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab.SignalReportBuilder.MeasurementRow;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Provides independent mapping-specific LINQ for the synthetic acquisition report.</summary>
internal static class SignalReportReference
{
    /// <summary>Retains the real sample's sources, time grain, direct-edge periods, and same-owner metadata.</summary>
    internal static IQueryable<SignalReportTotal> Build(
        SignalLabContext context,
        ValidatedSignalReport request
    )
    {
        var tenant = request.TenantId;
        var treeId = request.TreeId;
        var anchors = request.AnchorIds;
        var channels = request.ChannelIds;
        var sources = request.Sources;
        var granularity = request.Granularity;
        var from = request.FromInclusive;
        var until = request.UntilExclusive;
        var nodes = context
            .Nodes
            .AsNoTracking()
            .Where(node => node.TenantId == tenant && EF.Property<Guid>(node, SignalStorage.TreeId) == treeId);

        // WHY: Each acquisition port has its own physical layout. Project only the shared scalar grain;
        // Concat preserves independent events with equal owner, channel, timestamp and measured value.
        var measurements = context
            .PulseRecords
            .Where(value => value.TenantId == tenant)
            .Select(value => new MeasurementRow
            {
                TenantId = value.TenantId,
                NodeId = value.NodeId,
                ChannelId = value.ChannelId,
                Granularity = value.Granularity,
                Day = value.Day,
                Value = value.PulseCount,
                Source = CapturePort.Pulse,
            })
            .Concat(
                context
                    .SweepRecords
                    .Where(value => value.TenantId == tenant)
                    .Select(value => new MeasurementRow
                    {
                        TenantId = value.TenantId,
                        NodeId = value.ReceiverId,
                        ChannelId = value.StreamId,
                        Granularity = value.Resolution,
                        Day = value.CompletedOn,
                        Value = value.IntegratedTicks,
                        Source = CapturePort.Sweep,
                    }))
            .Concat(
                context
                    .BurstRecords
                    .Where(value => value.TenantId == tenant)
                    .Select(value => new MeasurementRow
                    {
                        TenantId = value.TenantId,
                        NodeId = value.DeviceId,
                        ChannelId = value.SignalId,
                        Granularity = value.BucketSize,
                        Day = value.CapturedOn,
                        Value = value.Magnitude,
                        Source = CapturePort.Burst,
                    }))
            .Where(value => Enumerable.Contains(channels, value.ChannelId)
                && Enumerable.Contains(sources, value.Source)
                && value.Granularity == granularity
                && value.Day >= from
                && value.Day < until);

        // WHY: The API starts from measured owners. Give the independent reference the same sparse-fact
        // direction and finite coordinate windows rather than comparing against an avoidable broad expansion.
        var pairs = nodes
            .Where(node => measurements.Any(value => value.TenantId == node.TenantId && value.NodeId == node.Id))
            .SelectMany(
                node => nodes.Where(anchor =>
                    EF.Property<long>(node, SignalStorage.Left) >= EF.Property<long>(anchor, SignalStorage.Left)
                    && EF.Property<long>(node, SignalStorage.Right)
                    <= EF.Property<long>(anchor, SignalStorage.Right)
                    && EF.Property<long>(node, SignalStorage.Left) < EF.Property<long>(anchor, SignalStorage.Right)
                    && EF.Property<long>(node, SignalStorage.Right)
                    > EF.Property<long>(anchor, SignalStorage.Left)),
                (node, anchor) => new
                {
                    Anchor = anchor,
                    Node = node
                })
            .Where(pair => Enumerable.Contains(anchors, pair.Anchor.Id));

        var contributions = pairs
            .Join(
                measurements,
                pair => new
                {
                    pair.Node.TenantId,
                    NodeId = pair.Node.Id,
                },
                value => new
                {
                    value.TenantId,
                    value.NodeId,
                },
                (
                    pair,
                    value
                ) => new
                {
                    pair.Anchor,
                    pair.Node,
                    Measurement = value,
                })
            .Join(
                context.SignalChannels.Where(channel => channel.TenantId == tenant
                    && Enumerable.Contains(channels, channel.Id)),
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
                (
                    row,
                    definition
                ) => new Contribution
                {
                    Anchor = row.Anchor,
                    Node = row.Node,
                    Measurement = row.Measurement,
                    MeasurementTypeId = definition.MeasurementTypeId,
                })
            .Where(row => row.Node.TypeId == row.MeasurementTypeId);

        // WHY: An ordinary row filter or an EXISTS over visible blockers is cheaper but semantically different:
        // count every visible direct edge and compare with stored depth to reject hidden intermediate nodes.
        contributions = contributions.Where(row => nodes.LongCount(child =>
                EF.Property<long>(child, SignalStorage.Left) > EF.Property<long>(row.Anchor, SignalStorage.Left)
                && EF.Property<long>(child, SignalStorage.Left) <= EF.Property<long>(row.Node, SignalStorage.Left)
                && EF.Property<long>(child, SignalStorage.Right)
                >= EF.Property<long>(row.Node, SignalStorage.Right)
                && EF.Property<long>(child, SignalStorage.Right)
                < EF.Property<long>(row.Anchor, SignalStorage.Right)
                && nodes.Any(parent => parent.Id == EF.Property<Guid?>(child, SignalStorage.ParentId)
                    && (long)EF.Property<int>(child, SignalStorage.Depth)
                    == (long)EF.Property<int>(parent, SignalStorage.Depth) + 1
                    && EF.Property<long>(parent, SignalStorage.Left)
                    < EF.Property<long>(child, SignalStorage.Left)
                    && EF.Property<long>(parent, SignalStorage.Right)
                    > EF.Property<long>(child, SignalStorage.Right)
                    && EF.Property<long>(parent, SignalStorage.Left)
                    >= EF.Property<long>(row.Anchor, SignalStorage.Left)
                    && EF.Property<long>(parent, SignalStorage.Right)
                    <= EF.Property<long>(row.Anchor, SignalStorage.Right)
                    && context.LinkAvailability.Any(period => period.TenantId == tenant
                        && period.ParentId == parent.Id
                        && period.ChildId == child.Id
                        && period.FromDate <= row.Measurement.Day
                        && (period.ToDate == null || period.ToDate >= row.Measurement.Day))))
            == (long)EF.Property<int>(row.Node, SignalStorage.Depth)
            - EF.Property<int>(row.Anchor, SignalStorage.Depth));

        foreach (var group in request.Groups)
        {
            contributions = ApplyMetadata(context, request, contributions, nodes, group);
        }

        return contributions
            .GroupBy(row => new
            {
                AnchorId = row.Anchor.Id,
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

    /// <summary>Shares domain criteria while independently constructing owner/path SQL.</summary>
    private static IQueryable<Contribution> ApplyMetadata(
        SignalLabContext context,
        ValidatedSignalReport request,
        IQueryable<Contribution> source,
        IQueryable<SignalNode> nodes,
        TagGroup group
    )
    {
        var type = group.TypeId;
        var predicate = SignalReportBuilder.OwnerPredicate(context, request.TenantId, group);
        Expression<Func<Contribution, SignalNode, bool>> contains = (row, owner)
            => EF.Property<long>(owner, SignalStorage.Left) <= EF.Property<long>(row.Node, SignalStorage.Left)
            && EF.Property<long>(owner, SignalStorage.Right) >= EF.Property<long>(row.Node, SignalStorage.Right)
            && EF.Property<long>(row.Node, SignalStorage.Left) < EF.Property<long>(owner, SignalStorage.Right)
            && EF.Property<long>(row.Node, SignalStorage.Right) > EF.Property<long>(owner, SignalStorage.Left);

        var rowParameter = predicate.Parameters[0];
        var ownerParameter = predicate.Parameters[1];
        var body = Expression.AndAlso(
            new ParameterReplacer(contains.Parameters, predicate.Parameters).Visit(contains.Body),
            predicate.Body);

        if (group.Mode == TagOwnerMode.Nearest)
        {
            Expression<Func<Contribution, SignalNode, bool>> nearest = (row, owner) => owner.Id == nodes
                    .Where(candidate => candidate.TypeId == type
                        && EF.Property<long>(candidate, SignalStorage.Left)
                        <= EF.Property<long>(row.Node, SignalStorage.Left)
                        && EF.Property<long>(candidate, SignalStorage.Right)
                        >= EF.Property<long>(row.Node, SignalStorage.Right)
                        && EF.Property<long>(row.Node, SignalStorage.Left)
                        < EF.Property<long>(candidate, SignalStorage.Right)
                        && EF.Property<long>(row.Node, SignalStorage.Right)
                        > EF.Property<long>(candidate, SignalStorage.Left))
                    .OrderByDescending(candidate => EF.Property<int>(candidate, SignalStorage.Depth))
                    .Select(candidate => (Guid?)candidate.Id)
                    .FirstOrDefault()
                && nodes.LongCount(child =>
                    EF.Property<long>(child, SignalStorage.Left) > EF.Property<long>(owner, SignalStorage.Left)
                    && EF.Property<long>(child, SignalStorage.Left)
                    <= EF.Property<long>(row.Node, SignalStorage.Left)
                    && EF.Property<long>(child, SignalStorage.Right)
                    >= EF.Property<long>(row.Node, SignalStorage.Right)
                    && EF.Property<long>(child, SignalStorage.Right)
                    < EF.Property<long>(owner, SignalStorage.Right)
                    && nodes.Any(parent => parent.Id == EF.Property<Guid?>(child, SignalStorage.ParentId)
                        && (long)EF.Property<int>(child, SignalStorage.Depth)
                        == (long)EF.Property<int>(parent, SignalStorage.Depth) + 1
                        && EF.Property<long>(parent, SignalStorage.Left)
                        < EF.Property<long>(child, SignalStorage.Left)
                        && EF.Property<long>(parent, SignalStorage.Right)
                        > EF.Property<long>(child, SignalStorage.Right)
                        && EF.Property<long>(parent, SignalStorage.Left)
                        >= EF.Property<long>(owner, SignalStorage.Left)
                        && EF.Property<long>(parent, SignalStorage.Right)
                        <= EF.Property<long>(owner, SignalStorage.Right)))
                == (long)EF.Property<int>(row.Node, SignalStorage.Depth)
                - EF.Property<int>(owner, SignalStorage.Depth);

            body = Expression.AndAlso(
                body,
                new ParameterReplacer(nearest.Parameters, predicate.Parameters).Visit(nearest.Body)!);
        }

        // WHY: The captured MySQL plan pulled owner EXISTS into the outer event join before its node
        // constraints, multiplying unrelated tags and events. A scalar owner count retains correlation
        // in this independent reference; positivity preserves the same owner-existence predicate.
        var count = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.LongCount),
            [typeof(SignalNode)],
            nodes.Expression,
            Expression.Quote(Expression.Lambda<Func<SignalNode, bool>>(body, ownerParameter)));

        var exists = Expression.GreaterThan(count, Expression.Constant(0L));

        return source.Where(Expression.Lambda<Func<Contribution, bool>>(exists, rowParameter));
    }

    /// <summary>Rebinds only the two known lambda parameters; no delegate or database query is executed.</summary>
    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly IReadOnlyList<ParameterExpression> _from;
        private readonly IReadOnlyList<ParameterExpression> _to;

        /// <summary>Retains exact parameter identities for this one composition.</summary>
        internal ParameterReplacer(
            IReadOnlyList<ParameterExpression> from,
            IReadOnlyList<ParameterExpression> to
        )
        {
            _from = from;
            _to = to;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(
            ParameterExpression node
        )
        {
            // WHY: Keep parameter-identity lookup aligned with its replacement list without an intermediate
            // sequence.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < _from.Count; index++)
            {
                if (ReferenceEquals(node, _from[index]))
                {
                    return _to[index];
                }
            }

            return node;
        }
    }
}
