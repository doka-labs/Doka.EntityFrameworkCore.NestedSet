namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal sealed partial record SignalReportDataset
{
    /// <summary>Persists the fixed, case-sensitive field catalog and two compatible additive channels.</summary>
    private static async Task SeedCatalogAsync(
        SignalLabContext context,
        Guid tenant,
        CancellationToken cancellationToken
    )
    {
        context.NodeKinds.AddRange(
            new NodeKind
            {
                TenantId = tenant,
                Id = StationKind,
                Code = "Station",
            },
            new NodeKind
            {
                TenantId = tenant,
                Id = ArrayKind,
                Code = "Array",
            },
            new NodeKind
            {
                TenantId = tenant,
                Id = ModuleKind,
                Code = "Module",
            },
            new NodeKind
            {
                TenantId = tenant,
                Id = SensorKind,
                Code = "Sensor",
            });

        (Guid Type, string Field, string[] Values)[] fields =
        [
            (ArrayKind, "Band", ["Narrow", "Wide"]),
            (ModuleKind, "ModuleClass", ["Receiver", "Relay"]),
            (SensorKind, "Cooling", ["Liquid", "Air"]),
            (SensorKind, "CaptureMode", ["Continuous", "Triggered"]),
            (SensorKind, "RackCode", ["R7", "R9", "x' OR 1=1 --"]),
        ];

        foreach (var (type, field, values) in fields)
        {
            context.TagDefinitions.Add(
                new TagDefinition
                {
                    TenantId = tenant,
                    TypeId = type,
                    FieldCode = field,
                    Label = field,
                });

            foreach (var value in values)
            {
                context.TagChoices.Add(
                    new TagChoice
                    {
                        TenantId = tenant,
                        TypeId = type,
                        FieldCode = field,
                        Value = value,
                    });
            }
        }

        context.SignalChannels.AddRange(
            new SignalChannel
            {
                TenantId = tenant,
                Id = PulseChannel,
                MeasurementTypeId = SensorKind,
                Name = "Pulse ticks",
                UnitCode = "count",
            },
            new SignalChannel
            {
                TenantId = tenant,
                Id = SweepChannel,
                MeasurementTypeId = SensorKind,
                Name = "Sweep ticks",
                UnitCode = "count",
            });

        await context.SaveChangesAsync(cancellationToken);
    }
}
