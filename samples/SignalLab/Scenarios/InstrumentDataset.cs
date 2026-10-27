namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Provides deterministic anchors for one independently runnable instrument scenario.</summary>
/// <param name="TreeId">The primary station tree identity.</param>
/// <param name="RelatedTreeId">A second station in the same laboratory.</param>
/// <param name="Total">The selected station root.</param>
/// <param name="ReceiverArray">The array containing two sensor counters.</param>
/// <param name="CalibrationArray">The sibling array containing a reference probe.</param>
/// <param name="PrimarySensor">The sensor used for payload updates and cycle rejection.</param>
internal sealed record InstrumentDataset(
    Guid TreeId,
    Guid RelatedTreeId,
    Instrument Total,
    Instrument ReceiverArray,
    Instrument CalibrationArray,
    Instrument PrimarySensor
)
{
    /// <summary>Gets the application-owned laboratory scope without requiring another entity.</summary>
    public static Guid ProjectId { get; } = Guid.Parse("5a1ab001-0000-4000-8000-000000000001");

    /// <summary>Gets the independent laboratory scope used by the isolation control.</summary>
    public static Guid OtherProjectId { get; } = Guid.Parse("5a1ab002-0000-4000-8000-000000000001");

    /// <summary>Creates a station, a second station and a separate laboratory through the public facade.</summary>
    /// <param name="context">The scenario-owned context connected to the initialized sample database.</param>
    /// <param name="scenarioNumber">The deterministic identity prefix for the selected scenario.</param>
    /// <param name="scenarioName">The label retained in the persisted roots for inspection.</param>
    /// <param name="cancellationToken">The token for hierarchy and database operations.</param>
    /// <returns>The inserted anchors used by the scenario.</returns>
    public static async Task<InstrumentDataset> CreateAsync(
        SignalLabContext context,
        int scenarioNumber,
        string scenarioName,
        CancellationToken cancellationToken
    )
    {
        var treeId = CreateId(scenarioNumber, 100);
        var relatedTreeId = CreateId(scenarioNumber, 101);
        var instruments = context
            .NestedSet<Instrument>()
            .ForScope(ProjectId);

        var station = CreateInstrument(scenarioNumber, 1, $"Station [{scenarioName}]");
        var receiver = CreateInstrument(scenarioNumber, 2, "Receiver array");
        var optical = CreateInstrument(scenarioNumber, 3, "Optical module");
        var primary = CreateInstrument(scenarioNumber, 4, "Primary sensor", 120);
        var secondary = CreateInstrument(scenarioNumber, 5, "Secondary sensor", 45);
        var calibration = CreateInstrument(scenarioNumber, 6, "Calibration array");
        var reference = CreateInstrument(scenarioNumber, 7, "Reference probe", 80);

        await instruments.InsertRootAsync(station, treeId, cancellationToken);
        await instruments.InsertChildAsync(receiver, station.NodeId, cancellationToken);
        await instruments.InsertChildAsync(optical, receiver.NodeId, cancellationToken);
        await instruments.InsertChildAsync(primary, optical.NodeId, cancellationToken);
        await instruments.InsertChildAsync(secondary, optical.NodeId, cancellationToken);
        await instruments.InsertChildAsync(calibration, station.NodeId, cancellationToken);
        await instruments.InsertChildAsync(reference, calibration.NodeId, cancellationToken);

        var otherStation = CreateInstrument(scenarioNumber, 8, $"Independent station [{scenarioName}]");
        await instruments.InsertRootAsync(otherStation, relatedTreeId, cancellationToken);
        await instruments.InsertChildAsync(
            CreateInstrument(scenarioNumber, 9, "Excluded probe", 444),
            otherStation.NodeId,
            cancellationToken);

        var otherLaboratory = context
            .NestedSet<Instrument>()
            .ForScope(OtherProjectId);

        var otherRoot = CreateInstrument(scenarioNumber, 10, $"Separate laboratory [{scenarioName}]");

        // WHY: Reusing TreeId in another scope makes the complete (Scope, TreeId) identity observable.
        await otherLaboratory.InsertRootAsync(otherRoot, treeId, cancellationToken);
        await otherLaboratory.InsertChildAsync(
            CreateInstrument(scenarioNumber, 11, "Isolated probe", 999),
            otherRoot.NodeId,
            cancellationToken);

        return new InstrumentDataset(treeId, relatedTreeId, station, receiver, calibration, primary);
    }

    /// <summary>Creates a device or container with a deterministic synthetic identity.</summary>
    /// <param name="scenarioNumber">The scenario prefix.</param>
    /// <param name="ordinal">The instrument number inside the scenario.</param>
    /// <param name="title">The displayed instrument label.</param>
    /// <param name="pulses">The leaf counter, or null for a container.</param>
    /// <returns>A detached instrument ready for explicit insertion.</returns>
    private static Instrument CreateInstrument(
        int scenarioNumber,
        byte ordinal,
        string title,
        int? pulses = null
    ) => new()
    {
        NodeId = CreateId(scenarioNumber, ordinal),
        Title = title,
        PulseCount = pulses,
    };

    /// <summary>Produces fixed fake keys for repeatable reset output without prescribing application key generation.</summary>
    /// <param name="scenarioNumber">The sample-local scenario prefix.</param>
    /// <param name="ordinal">The instrument number within the scenario.</param>
    /// <returns>A deterministic synthetic identity.</returns>
    private static Guid CreateId(
        int scenarioNumber,
        byte ordinal
    ) => new(scenarioNumber, 0x51ab, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, ordinal);
}
