using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal sealed partial record CompositionConsumerDataset
{

    /// <summary>Creates the sample's real typed catalogs and at most 1,200 physical fact rows.</summary>
    private async Task SeedSignalsAsync(
        CompositionCapacitySignalLabContext context,
        CancellationToken cancellationToken
    )
    {
        var tenant = CompositionConsumerModel.Tenant;
        var type = CompositionConsumerModel.Identity(1);
        context.NodeKinds.Add(
            new SignalApp.NodeKind
            {
                TenantId = tenant,
                Id = type,
                Code = "Capacity",
            });

        foreach (var field in SignalFields)
        {
            context.TagDefinitions.Add(
                new SignalApp.TagDefinition
                {
                    TenantId = tenant,
                    TypeId = type,
                    FieldCode = field,
                    Label = field,
                });
        }

        foreach (var field in SignalFields)
        {
            foreach (var value in s_metadataChoices)
            {
                context.TagChoices.Add(
                    new SignalApp.TagChoice
                    {
                        TenantId = tenant,
                        TypeId = type,
                        FieldCode = field,
                        Value = value,
                    });
            }

            foreach (var key in Facts.Prepend(1))
            {
                context.TagValues.Add(
                    new SignalApp.TagValue
                    {
                        TenantId = tenant,
                        NodeId = CompositionConsumerModel.Identity(key),
                        TypeId = type,
                        FieldCode = field,
                        Value = field == "ModuleClass" && key != 1 && key % 2 != 0
                            ? "no"
                            : "yes",
                    });
            }
        }

        for (var metric = 101; metric <= 102; metric++)
        {
            var metricId = CompositionConsumerModel.Identity(metric);
            context.SignalChannels.Add(
                new SignalApp.SignalChannel
                {
                    TenantId = tenant,
                    Id = metricId,
                    MeasurementTypeId = type,
                    Name = "Capacity metric",
                    UnitCode = "unit",
                });

            foreach (var key in Facts)
            {
                var node = CompositionConsumerModel.Identity(key);

                for (var day = 0; day < 2; day++)
                {
                    var date = CompositionConsumerModel.FirstDay.AddDays(day);
                    var value = (metric - 100) * 10 + day + key % 7;
                    context.PulseRecords.Add(
                        new SignalApp.PulseRecord
                        {
                            TenantId = tenant,
                            ChannelId = metricId,
                            NodeId = node,
                            Granularity = SignalApp.MeasurementGranularity.Daily,
                            Day = date,
                            PulseCount = value,
                        });

                    context.SweepRecords.Add(
                        new SignalApp.SweepRecord
                        {
                            TenantId = tenant,
                            StreamId = metricId,
                            ReceiverId = node,
                            Resolution = SignalApp.MeasurementGranularity.Daily,
                            CompletedOn = date,
                            IntegratedTicks = value * 2,
                        });

                    context.BurstRecords.Add(
                        new SignalApp.BurstRecord
                        {
                            TenantId = tenant,
                            SignalId = metricId,
                            DeviceId = node,
                            BucketSize = SignalApp.MeasurementGranularity.Daily,
                            CapturedOn = date,
                            Magnitude = value * 3,
                        });
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }
}
