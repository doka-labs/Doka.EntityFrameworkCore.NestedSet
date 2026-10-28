using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Extends the actual sample's fixed catalog with a deterministic measured reporting branch.</summary>
internal static class SignalBenchmarkData
{
    /// <summary>Seeds the selected physical node count, retaining the sample's isolation and control nodes.</summary>
    internal static async Task<SignalReportDataset> CreateAsync(
        SignalLabContext context,
        int nodes,
        BenchmarkShape shape
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodes, 30);
        var data = await SignalReportDataset.CreateAsync(context, 5, false, CancellationToken.None);
        var count = nodes - SignalReportDataset.SeedNodeCount;
        var entities = new SignalNode[count];
        var branches = new NestedSetBranch<SignalNode>[count];
        var children = new List<int>?[count];
        var parents = new int?[count];

        for (var index = 0; index < count; index++)
        {
            entities[index] = new SignalNode
            {
                TenantId = data.TenantId,
                Id = SignalReportDataset.Id(10_000 + index),
                TypeId = SignalReportDataset.SensorKind,
                Name = FormattableString.Invariant($"Measured {index:D9}"),
            };

            if (index == 0)
            {
                continue;
            }

            var parent = shape switch
            {
                BenchmarkShape.Wide => 0,
                BenchmarkShape.Deep => index - 1,
                BenchmarkShape.Balanced => (index - 1) / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };

            parents[index] = parent;
            (children[parent] ??= []).Add(index);
        }

        for (var index = count - 1; index >= 0; index--)
        {
            branches[index] = new NestedSetBranch<SignalNode>(
                entities[index],
                children[index]
                    ?.Select(child => branches[child])
                    .ToArray());
        }

        await context
            .NestedSet<SignalNode>()
            .ForScope(data.TenantId)
            .InsertSubtreeAsync(branches[0], SignalReportDataset.Id(3), CancellationToken.None);

        var channels = new[] { SignalReportDataset.PulseChannel, SignalReportDataset.SweepChannel };

        // WHY: Keep each entity aligned with its parent ordinal when creating periods and metadata.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < count; index++)
        {
            var entity = entities[index];
            context.LinkAvailability.Add(
                new LinkWindow
                {
                    TenantId = data.TenantId,
                    ChildId = entity.Id,
                    ParentId = parents[index] is { } parent ? entities[parent].Id : SignalReportDataset.Id(3),
                    FromDate = SignalReportDataset.FirstDay,
                });

            context.TagValues.AddRange(
                new TagValue
                {
                    TenantId = data.TenantId,
                    NodeId = entity.Id,
                    TypeId = entity.TypeId,
                    FieldCode = "Cooling",
                    Value = "Liquid",
                },
                new TagValue
                {
                    TenantId = data.TenantId,
                    NodeId = entity.Id,
                    TypeId = entity.TypeId,
                    FieldCode = "RackCode",
                    Value = "R7",
                });

            // WHY: These are independent per-node measurements, not stored intermediate subtree totals.
            // Keeping all three physical formats and both channels preserves the selected acquisition work.
            foreach (var channel in channels)
            {
                context.PulseRecords.Add(
                    new PulseRecord
                    {
                        TenantId = data.TenantId,
                        NodeId = entity.Id,
                        ChannelId = channel,
                        Day = SignalReportDataset.FirstDay,
                        Granularity = MeasurementGranularity.Daily,
                        PulseCount = 1,
                    });

                context.SweepRecords.Add(
                    new SweepRecord
                    {
                        TenantId = data.TenantId,
                        ReceiverId = entity.Id,
                        StreamId = channel,
                        CompletedOn = SignalReportDataset.FirstDay,
                        Resolution = MeasurementGranularity.Daily,
                        IntegratedTicks = 2,
                    });

                context.BurstRecords.Add(
                    new BurstRecord
                    {
                        TenantId = data.TenantId,
                        DeviceId = entity.Id,
                        SignalId = channel,
                        CapturedOn = SignalReportDataset.FirstDay,
                        BucketSize = MeasurementGranularity.Daily,
                        Magnitude = 3,
                    });
            }

            if ((index + 1) % 64 == 0)
            {
                // WHY: Payload setup must not retain every measurement and metadata entry in the tracker
                // merely because the observational benchmark selected a larger structural population.
                await context.SaveChangesAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        BenchmarkFixture.Require(
            await context
                .Nodes
                .IgnoreQueryFilters()
                .LongCountAsync(CancellationToken.None)
            == nodes,
            "SignalLab benchmark population differs from its Nodes parameter.");

        return data;
    }
}
