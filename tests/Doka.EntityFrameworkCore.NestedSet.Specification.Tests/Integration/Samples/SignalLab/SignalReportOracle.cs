using Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
/// Computes the fixed report expectations by walking adjacency, without copying SQL bounds or count expressions.
/// </summary>
internal static class SignalReportOracle
{
    /// <summary>
    /// Evaluates all selected anchors, metrics and sources against independently described direct edges and facts.
    /// </summary>
    internal static SignalReportCell[] Calculate(SignalReportRequest request)
    {
        Dictionary<int, int?> parents = new() { [1] = null, [2] = 1, [3] = 2, [4] = 3, [5] = 3 };
        Dictionary<int, Guid> types = new()
        {
            [1] = SignalReportDataset.StationKind, [2] = SignalReportDataset.ArrayKind,
            [3] = SignalReportDataset.ModuleKind,
            [4] = SignalReportDataset.SensorKind,
            [5] = SignalReportDataset.SensorKind,
        };

        Dictionary<(int Node, string Field), string> metadata = new()
        {
            [(2, "Band")] = "Narrow", [(3, "ModuleClass")] = "Receiver",
            [(4, "Cooling")] = "Liquid", [(4, "RackCode")] = "R7",
            [(5, "Cooling")] = "Liquid", [(5, "RackCode")] = "R7",
        };

        (int Node, int Day, double Value, MeasurementGranularity Grain)[] facts =
        [
            (4, 0, 10, MeasurementGranularity.Daily), (4, 1, 20, MeasurementGranularity.Daily),
            (5, 0, 40, MeasurementGranularity.Daily), (5, 1, 50, MeasurementGranularity.Daily),
            (3, 0, 1000, MeasurementGranularity.Daily), (4, 0, 9000, MeasurementGranularity.Monthly),
        ];

        var cells = new List<SignalReportCell>();
        foreach (var anchorId in request.AnchorIds.Distinct())
        {
            var anchor = parents.Keys.Single(node => SignalReportDataset.Id(node) == anchorId);
            foreach (var signal in request.ChannelIds.Distinct())
            {
                foreach (var source in request.Sources.Distinct())
                {
                    var hasData = false;
                    var sum = 0d;
                    foreach (var fact in facts)
                    {
                        var day = SignalReportDataset.FirstDay.AddDays(fact.Day);
                        if (day < request.FromInclusive || day >= request.UntilExclusive
                            || fact.Grain != request.Granularity || types[fact.Node] != SignalReportDataset.SensorKind
                            || !ReachesAnchor(fact.Node, anchor, day) || !MetadataMatches(fact.Node))
                        {
                            continue;
                        }

                        hasData = true;
                        var metric = signal == SignalReportDataset.PulseChannel ? 1 : 2;
                        sum += fact.Value * metric * ((int)source + 1);
                    }

                    cells.Add(new SignalReportCell(anchorId, signal, source, hasData, sum));
                }
            }
        }

        return cells.ToArray();

        bool ReachesAnchor(int node, int anchor, DateOnly day)
        {
            for (int? current = node; current is { } child; current = parents[child])
            {
                if (child == anchor)
                {
                    return true;
                }

                // WHY: The direct B edge alone expires. Returning before this check excludes an anchor's incoming edge.
                if (parents[child] is null || day < new DateOnly(2042, 1, 1)
                    || (child == 5 && day > SignalReportDataset.FirstDay))
                {
                    return false;
                }
            }

            return false;
        }

        bool MetadataMatches(int node)
        {
            foreach (var group in request.Criteria.GroupBy(criterion => criterion.TypeId))
            {
                var found = false;
                for (int? current = node; current is { } owner; current = parents[owner])
                {
                    if (types[owner] != group.Key)
                    {
                        continue;
                    }

                    found = group.All(criterion => metadata.TryGetValue((owner, criterion.FieldCode), out var stored)
                        && string.Equals(stored, criterion.Value, StringComparison.Ordinal));
                    if (found || group.First().OwnerMode == TagOwnerMode.Nearest)
                    {
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
