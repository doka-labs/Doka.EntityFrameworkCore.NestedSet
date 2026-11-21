namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

internal sealed partial record SignalReportDataset
{
    /// <summary>Stores per-node fields, including a deliberate split-owner mismatch in the nearest example.</summary>
    private static void AddMetadata(
        SignalLabContext context,
        Guid tenant,
        bool nearest
    )
    {
        if (!nearest)
        {
            Add(2, ArrayKind, "Band", "Narrow");
        }

        Add(3, ModuleKind, "ModuleClass", "Receiver");
        Add(
            nearest
                ? 5
                : 4,
            SensorKind,
            "CaptureMode",
            "Continuous");

        Add(
            nearest
                ? 6
                : 5,
            SensorKind,
            "CaptureMode",
            "Continuous");

        if (nearest)
        {
            Add(4, ModuleKind, "ModuleClass", "Relay");
            Add(5, SensorKind, "Cooling", "Air");
            Add(5, SensorKind, "RackCode", "R7");
            Add(6, SensorKind, "Cooling", "Liquid");
            Add(6, SensorKind, "RackCode", "R9");
        }
        else
        {
            Add(4, SensorKind, "Cooling", "Liquid");
            Add(4, SensorKind, "RackCode", "R7");
            Add(5, SensorKind, "Cooling", "Liquid");
            Add(5, SensorKind, "RackCode", "R7");
        }

        return;

        void Add(
            int node,
            Guid type,
            string field,
            string value
        ) => context.TagValues.Add(
            new TagValue
            {
                TenantId = tenant,
                NodeId = Id(node),
                TypeId = type,
                FieldCode = field,
                Value = value,
            });
    }
}
