using SignalApp = Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

internal static partial class CompositionConsumerCapacityProfiles
{
    /// <summary>Walks only the known adjacency for independent expected root distances.</summary>
    private static int Depth(
        int key,
        bool balanced,
        bool deep
    )
    {
        var depth = 0;

        while (CompositionConsumerDataset.Parent(key, balanced, deep) is { } parent)
        {
            depth++;
            key = parent;
        }

        return depth;
    }

    /// <summary>Calculates independent sums with per-day pruning and nearest-owner rejection.</summary>
    private static string[] ExpectedSignals(
        CompositionConsumerDataset data,
        SignalApp.SignalReportRequest request,
        bool balanced,
        bool deep
    )
    {
        var rows = new List<string>();
        var days = request.UntilExclusive.DayNumber - request.FromInclusive.DayNumber;
        var nearest = request.Criteria[0].OwnerMode == SignalApp.TagOwnerMode.Nearest;

        foreach (var anchorId in request.AnchorIds)
        {
            var anchor = CompositionConsumerModel.Integer(anchorId);

            foreach (var metricId in request.ChannelIds)
            {
                var metric = CompositionConsumerModel.Integer(metricId);

                foreach (var source in request.Sources)
                {
                    var total = 0d;
                    var found = false;

                    foreach (var fact in data.Facts)
                    {
                        if (!CompositionConsumerDataset.Contains(anchor, fact, balanced, deep)
                            || (nearest && fact % 2 != 0))
                        {
                            continue;
                        }

                        for (var day = 0; day < days; day++)
                        {
                            if (day > 0
                                && anchor != 2
                                && CompositionConsumerDataset.Contains(anchor, 2, balanced, deep)
                                && CompositionConsumerDataset.Contains(2, fact, balanced, deep))
                            {
                                continue;
                            }

                            found = true;
                            total += ((metric - 100) * 10 + day + fact % 7) * ((int)source + 1);
                        }
                    }

                    rows.Add(SignalKey(new SignalApp.SignalReportCell(anchorId, metricId, source, found, total)));
                }
            }
        }

        return rows
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Canonicalizes only the small aggregate output, with exact integer-valued sample sums.</summary>
    private static string SignalKey(
        SignalApp.SignalReportCell row
    ) =>
        $"{CompositionConsumerModel.Integer(row.AnchorId)}:{CompositionConsumerModel.Integer(row.ChannelId)}:"
        + $"{(int)row.Source}:{row.HasData}:"
        + row.Value.ToString("R", CultureInfo.InvariantCulture);
}
