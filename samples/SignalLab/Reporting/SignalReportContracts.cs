namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Identifies independent physical acquisition formats in the synthetic laboratory.</summary>
internal enum CapturePort
{
    /// <summary>Reads native pulse-counter events.</summary>
    Pulse,

    /// <summary>Reads completed receiver integration windows.</summary>
    Sweep,

    /// <summary>Reads device-specific burst magnitudes.</summary>
    Burst,
}

/// <summary>Selects a metadata owner independently of the predicate applied to its values.</summary>
internal enum TagOwnerMode
{
    /// <summary>Accepts any visible matching ancestor or the node itself.</summary>
    Any,

    /// <summary>Checks only the structurally nearest owner of the requested classification.</summary>
    Nearest,
}

/// <summary>Names the sample's deliberately small scalar operator contract.</summary>
internal enum TagOperator
{
    /// <summary>Compares fixed codes and values with case-sensitive equality.</summary>
    Equal,
}

/// <summary>Specifies one scalar field on one owner classification.</summary>
/// <param name="TypeId">The required owner classification.</param>
/// <param name="FieldCode">The exact catalog field code.</param>
/// <param name="Value">The fixed permitted value, passed to SQL as a parameter.</param>
/// <param name="OwnerMode">The owner selection policy.</param>
/// <param name="Operator">The allowed scalar comparison.</param>
internal sealed record TagCriterion(
    Guid TypeId,
    string FieldCode,
    string Value,
    TagOwnerMode OwnerMode = TagOwnerMode.Any,
    TagOperator Operator = TagOperator.Equal
);

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

/// <summary>Stores one scalar SQL aggregate, retaining source as part of its grain.</summary>
internal sealed class SignalReportTotal
{
    /// <summary>Gets the subtree anchor identity.</summary>
    public Guid AnchorId { get; init; }

    /// <summary>Gets the channel identity.</summary>
    public Guid ChannelId { get; init; }

    /// <summary>Gets the independent physical source.</summary>
    public CapturePort Source { get; init; }

    /// <summary>Gets the sum of qualifying measurement rows.</summary>
    public double Value { get; init; }
}

/// <summary>Distinguishes an absent group from a measured mathematical zero.</summary>
/// <param name="AnchorId">The selected subtree anchor.</param>
/// <param name="ChannelId">The selected channel.</param>
/// <param name="Source">The selected measurement source.</param>
/// <param name="HasData">Whether SQL returned a qualifying group.</param>
/// <param name="Value">The aggregate, or the presentation zero for an absent group.</param>
internal sealed record SignalReportCell(
    Guid AnchorId,
    Guid ChannelId,
    CapturePort Source,
    bool HasData,
    double Value
);

/// <summary>Captures one validated classification's same-owner scalar predicates.</summary>
/// <param name="TypeId">The owner classification.</param>
/// <param name="Mode">The common owner selection policy.</param>
/// <param name="Criteria">Deduplicated, noncontradictory scalar fields.</param>
internal sealed record TagGroup(
    Guid TypeId,
    TagOwnerMode Mode,
    TagCriterion[] Criteria
);

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

/// <summary>Flattens a batched catalog read without materializing domain entities.</summary>
internal sealed class SignalCatalogRow
{
    /// <summary>Gets the row kind: anchor, channel, field definition, or allowed choice.</summary>
    public int Kind { get; init; }

    /// <summary>Gets an anchor or channel identity when relevant.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets a metadata owner or measurement classification.</summary>
    public Guid TypeId { get; init; }

    /// <summary>Gets a field code, or the empty string for another row kind.</summary>
    public string FieldCode { get; init; } = string.Empty;

    /// <summary>Gets a fixed allowed choice, or the empty string.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Gets a channel's additive unit.</summary>
    public string UnitCode { get; init; } = string.Empty;
}
