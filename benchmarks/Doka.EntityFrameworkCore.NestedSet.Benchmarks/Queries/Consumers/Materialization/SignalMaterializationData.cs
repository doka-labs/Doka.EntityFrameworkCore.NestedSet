using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Owns fixed scalar inputs and an independent complete output oracle outside measured execution.</summary>
/// <param name="Request">The validated request shared by both actual materialization readers.</param>
/// <param name="Catalog">The exact selected anchors, units, field, and allowed value.</param>
/// <param name="Totals">The ordered incoming scalar groups, independent of presentation order.</param>
/// <param name="Expected">The complete ordered selected grain, including missing and measured-zero cells.</param>
internal sealed record SignalMaterializationData(
    ValidatedSignalReport Request,
    SignalCatalogRow[] Catalog,
    SignalReportTotal[] Totals,
    SignalReportCell[] Expected
)
{
    /// <summary>
    /// Prepares the same deterministic grains and identities used by the isolated materializer comparison.
    /// </summary>
    internal static SignalMaterializationData Create(
        int cells,
        SignalMaterializationDensity density,
        bool interleavedSources
    )
    {
        var (anchors, channels) = cells switch
        {
            24 => (4, 2),
            6144 => (64, 32),
            _ => throw new ArgumentOutOfRangeException(nameof(cells), cells, "Select a 24- or 6144-cell grain."),
        };

        var totalCount = density switch
        {
            SignalMaterializationDensity.Empty => 0,
            SignalMaterializationDensity.Sparse => 2,
            SignalMaterializationDensity.Dense => cells,
            _ => throw new ArgumentOutOfRangeException(nameof(density), density, "Select a known group density."),
        };

        var request = SignalReportValidation.Snapshot(
            new SignalReportRequest(
                Id(1),
                Id(2),
                Enumerable
                    .Range(1, anchors)
                    .Select(Id)
                    .ToArray(),
                Enumerable
                    .Range(101, channels)
                    .Select(Id)
                    .ToArray(),
                [CapturePort.Sweep, CapturePort.Pulse, CapturePort.Burst],
                SignalReportDataset.FirstDay,
                SignalReportDataset.FirstDay.AddDays(2),
                MeasurementGranularity.Daily,
                [new TagCriterion(Id(3), "Band", "Narrow")]));

        var catalog = request
            .AnchorIds
            .Select(anchor => new SignalCatalogRow
            {
                Kind = 0,
                Id = anchor,
            })
            .Concat(
                request.ChannelIds.Select(channel => new SignalCatalogRow
                {
                    Kind = 1,
                    Id = channel,
                    UnitCode = "count",
                }))
            .Append(
                new SignalCatalogRow
                {
                    Kind = 2,
                    TypeId = Id(3),
                    FieldCode = "Band",
                })
            .Append(
                new SignalCatalogRow
                {
                    Kind = 3,
                    TypeId = Id(3),
                    FieldCode = "Band",
                    Value = "Narrow",
                })
            .ToArray();

        var grain = request
            .AnchorIds
            .SelectMany(anchor =>
                request.ChannelIds.SelectMany(channel =>
                    request.Sources.Select(source => (Anchor: anchor, Channel: channel, Source: source))))
            .ToArray();

        var totals = grain
            .Take(totalCount)
            .Select((
                key,
                index
            ) => new SignalReportTotal
            {
                AnchorId = key.Anchor,
                ChannelId = key.Channel,
                Source = key.Source,
                Value = index,
            })
            .Reverse()
            .ToArray();

        if (interleavedSources)
        {
            totals = totals
                .OrderBy(total => total.Source)
                .ThenBy(total => total.ChannelId)
                .ThenBy(total => total.AnchorId)
                .ToArray();
        }

        // WHY: The oracle follows selection positions and the requested density, not CreateGrid or the
        // reordered incoming totals. It independently distinguishes missing values from a measured zero.
        var expected = grain
            .Select((key, index) => new SignalReportCell(
                key.Anchor,
                key.Channel,
                key.Source,
                index < totalCount,
                index < totalCount ? index : 0))
            .ToArray();

        return new SignalMaterializationData(request, catalog, totals, expected);
    }

    /// <summary>Uses the synthetic laboratory's deterministic identities without importing application keys.</summary>
    private static Guid Id(
        int value
    ) => SignalReportDataset.Id(value);
}
