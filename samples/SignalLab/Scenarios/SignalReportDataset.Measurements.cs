namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal sealed partial record SignalReportDataset
{
    /// <summary>Adds the same fact keys independently to all sources and two additive channels.</summary>
    private static void AddMeasurements(
        SignalLabContext context,
        Guid tenant,
        Guid node,
        DateOnly day,
        double amount
    )
    {
        (Guid, int)[] channels = [(PulseChannel, 1), (SweepChannel, 2)];

        foreach (var (channel, multiplier) in channels)
        {
            context.PulseRecords.Add(
                new PulseRecord
                {
                    TenantId = tenant,
                    ChannelId = channel,
                    NodeId = node,
                    Granularity = MeasurementGranularity.Daily,
                    Day = day,
                    PulseCount = amount * multiplier,
                });

            context.SweepRecords.Add(
                new SweepRecord
                {
                    TenantId = tenant,
                    StreamId = channel,
                    ReceiverId = node,
                    Resolution = MeasurementGranularity.Daily,
                    CompletedOn = day,
                    IntegratedTicks = amount * multiplier * 2,
                });

            context.BurstRecords.Add(
                new BurstRecord
                {
                    TenantId = tenant,
                    SignalId = channel,
                    DeviceId = node,
                    BucketSize = MeasurementGranularity.Daily,
                    CapturedOn = day,
                    Magnitude = amount * multiplier * 3,
                });
        }
    }

    /// <summary>Stores independent daily facts and deliberate intermediate/grain counterexamples.</summary>
    private static void AddScenarioMeasurements(
        SignalLabContext context,
        Guid tenant,
        bool nearest
    )
    {
        if (nearest)
        {
            AddMeasurements(context, tenant, Id(6), FirstDay, 10);
        }
        else
        {
            AddMeasurements(context, tenant, Id(4), FirstDay, 10);
            AddMeasurements(context, tenant, Id(4), FirstDay.AddDays(1), 20);
            AddMeasurements(context, tenant, Id(5), FirstDay, 40);
            AddMeasurements(context, tenant, Id(5), FirstDay.AddDays(1), 50);

            // WHY: Stored intermediate and monthly aggregates deliberately exist; the report's type and grain exclude
            // them.
            context.PulseRecords.Add(
                new PulseRecord
                {
                    TenantId = tenant,
                    ChannelId = PulseChannel,
                    NodeId = Id(3),
                    Day = FirstDay,
                    Granularity = MeasurementGranularity.Daily,
                    PulseCount = 1000,
                });

            context.PulseRecords.Add(
                new PulseRecord
                {
                    TenantId = tenant,
                    ChannelId = PulseChannel,
                    NodeId = Id(4),
                    Day = FirstDay,
                    Granularity = MeasurementGranularity.Monthly,
                    PulseCount = 9000,
                });
        }
    }
}
