using System.Globalization;

namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Demonstrates bundled reporting, exact-edge validity and explicit metadata-owner policies.</summary>
internal static class ReportingScenario
{
    /// <summary>Runs one independent reporting dataset with readable selected rules and scalar results.</summary>
    internal static async Task RunAsync(
        SignalLabContext context,
        string scenario,
        bool details,
        CancellationToken cancellationToken
    )
    {
        var nearest = scenario == "nearest";
        var scenarioNumber = scenario switch
        {
            "report" => 5,
            "temporal" => 6,
            "metadata" => 7,
            "nearest" => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var data = await SignalReportDataset.CreateAsync(context, scenarioNumber, nearest, cancellationToken);
        await SampleConsole.HeadingAsync(
            $"{scenario}: current hierarchy, dated acquisition events, inclusive direct-link availability",
            cancellationToken);
        await SampleConsole.WriteLineAsync(
            "Each report reads one scalar catalog and one SQL aggregate inside one snapshot. "
            + "No facts or nodes are loaded by reporting.",
            cancellationToken);

        if (nearest)
        {
            await PrintAsync(
                context,
                data.Request(includeSensor: false),
                "Any Receiver module: Sensor P contributes 10",
                10,
                cancellationToken);
            await PrintAsync(
                context,
                data.Request(TagOwnerMode.Nearest, includeSensor: false),
                "Nearest Receiver module: Relay blocks fallback",
                null,
                cancellationToken);
            await PrintAsync(
                context,
                data.Request(TagOwnerMode.Nearest, "Relay", includeSensor: false),
                "Nearest Relay module: Sensor P contributes 10",
                10,
                cancellationToken);
            await PrintAsync(
                context,
                data.Request(moduleClass: "Relay"),
                "Same owner: Liquid on P plus R7 on Q cannot satisfy one sensor",
                null,
                cancellationToken);
        }
        else if (scenario == "temporal")
        {
            var request = data.Request() with
            {
                AnchorIds = [SignalReportDataset.Id(1), SignalReportDataset.Id(5)],
                FromInclusive = SignalReportDataset.FirstDay.AddDays(1),
            };

            await PrintAsync(
                context,
                request,
                "11 March 2042: Station loses B through its closed link; an anchor on B retains its own 50",
                20,
                cancellationToken);
        }
        else if (scenario == "metadata")
        {
            await PrintAsync(
                context,
                data.Request(),
                "Band Narrow AND ModuleClass Receiver AND sensor (Liquid AND R7), each kind on one owner",
                70,
                cancellationToken);
            var rejectedValues = data.Request() with
            {
                Criteria = [new TagCriterion(SignalReportDataset.ArrayKind, "Band", "Wide")],
            };

            await PrintAsync(
                context,
                rejectedValues,
                "Band Wide: no qualifying observation, distinct from measured zero",
                null,
                cancellationToken);
        }
        else
        {
            await PrintAsync(
                context,
                data.Request(),
                "10-11 March 2042: four anchors, two channels and three physical acquisition ports",
                70,
                cancellationToken);
        }

        if (details)
        {
            var structure = await context
                .NestedSet<SignalNode>()
                .ForScope(data.TenantId)
                .InTree(data.TreeId)
                .Nodes
                .Select(node => new
                {
                    node.Name,
                    node.Id,
                    Depth = EF.Property<int>(node, SignalStorage.Depth),
                    Left = EF.Property<long>(node, SignalStorage.Left),
                    Right = EF.Property<long>(node, SignalStorage.Right),
                })
                .ToArrayAsync(cancellationToken);

            foreach (var node in structure)
            {
                await SampleConsole.WriteLineAsync(
                    $"{new string(' ', node.Depth * 2)}{node.Name}: {node.Id}, bounds {node.Left}..{node.Right}",
                    cancellationToken);
            }
        }
    }

    /// <summary>Checks exact synthetic fixture totals and prints each overlapping anchor separately.</summary>
    private static async Task PrintAsync(
        SignalLabContext context,
        SignalReportRequest request,
        string description,
        double? expectedStationPulse,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(description, cancellationToken);
        var cells = await SignalReportBuilder.ReadAsync(context, request, cancellationToken);
        var station = cells.Single(cell => cell.AnchorId == SignalReportDataset.Id(1)
            && cell.ChannelId == SignalReportDataset.PulseChannel
            && cell.Source == CapturePort.Pulse);

        SampleConsole.Require(
            station.HasData == expectedStationPulse.HasValue,
            "The report did not match the documented fixture data availability.");

        if (expectedStationPulse is { } expected)
        {
            // WHY: These fixed small integer inputs and sums are exactly representable in double; reject any drift.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            SampleConsole.Require(
                station.Value == expected,
                "The report did not match the documented independent fixture total.");
        }

        foreach (var cell in cells)
        {
            var channel = cell.ChannelId == SignalReportDataset.PulseChannel ? "Pulse ticks" : "Sweep ticks";
            var value = cell.HasData ? cell.Value.ToString("0.##", CultureInfo.InvariantCulture) : "no data";
            await SampleConsole.WriteLineAsync(
                $"{SignalReportDataset.AnchorName(cell.AnchorId)}: {channel}, {cell.Source} = {value}",
                cancellationToken);
        }
    }
}
