namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Requests one tenant/tree report at one explicit additive grain.</summary>
/// <param name="TenantId">The authorized tenant selected by the application.</param>
/// <param name="TreeId">The authorized reporting tree.</param>
/// <param name="AnchorIds">The selected subtree anchors.</param>
/// <param name="ChannelIds">The selected additive channels.</param>
/// <param name="Sources">The independent measurement sources.</param>
/// <param name="FromInclusive">The first measurement day.</param>
/// <param name="UntilExclusive">The exclusive end of the measurement window.</param>
/// <param name="Granularity">The single requested measurement grain.</param>
/// <param name="Criteria">AND-combined metadata groups.</param>
internal sealed record SignalReportRequest(
    Guid TenantId,
    Guid TreeId,
    IReadOnlyList<Guid> AnchorIds,
    IReadOnlyList<Guid> ChannelIds,
    IReadOnlyList<CapturePort> Sources,
    DateOnly FromInclusive,
    DateOnly UntilExclusive,
    MeasurementGranularity Granularity,
    IReadOnlyList<TagCriterion> Criteria
);
