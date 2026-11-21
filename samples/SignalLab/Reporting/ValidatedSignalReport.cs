namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>
/// Holds private snapshots after syntax validation; catalog authorization is checked before execution.
/// </summary>
/// <param name="TenantId">The application-authorized tenant.</param>
/// <param name="TreeId">The explicitly bound tree.</param>
/// <param name="AnchorIds">The deduplicated private anchor selection.</param>
/// <param name="ChannelIds">The deduplicated channel selection.</param>
/// <param name="Sources">The deduplicated physical sources.</param>
/// <param name="FromInclusive">The first measurement day.</param>
/// <param name="UntilExclusive">The exclusive reporting boundary.</param>
/// <param name="Granularity">The single selected grain.</param>
/// <param name="Groups">The deduplicated same-owner field groups.</param>
internal sealed record ValidatedSignalReport(
    Guid TenantId,
    Guid TreeId,
    Guid[] AnchorIds,
    Guid[] ChannelIds,
    CapturePort[] Sources,
    DateOnly FromInclusive,
    DateOnly UntilExclusive,
    MeasurementGranularity Granularity,
    TagGroup[] Groups
);
