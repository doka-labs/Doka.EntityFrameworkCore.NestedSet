namespace Doka.EntityFrameworkCore.NestedSet.Samples.SignalLab;

/// <summary>Shares only the display, aggregate and validation steps used by multiple SignalLab scenarios.</summary>
internal static class InstrumentScenarioSupport
{
    /// <summary>Sums sensor-leaf pulses in the database without loading the aggregate anchor.</summary>
    /// <param name="instruments">The facade bound to the reporting project.</param>
    /// <param name="branchId">The aggregate whose strict descendants contribute measurements.</param>
    /// <param name="cancellationToken">The token for the SQL aggregate.</param>
    /// <returns>The synthetic pulse count, with missing leaf measurements treated as zero.</returns>
    public static Task<int> SumLeafPulsesAsync(
        ScopedNestedSet<Instrument, Guid> instruments,
        Guid branchId,
        CancellationToken cancellationToken
    ) // WHY: All sensor leaves measure the same unit and period; summing aggregate branches would double-count them.
        => instruments
            .DescendantsOf(branchId)
            .Where(instrument => instrument.End == instrument.Start + 1)
            .SumAsync(instrument => instrument.PulseCount ?? 0, cancellationToken);

    /// <summary>Prints one small tree in hierarchy preorder, with optional structural details.</summary>
    /// <param name="instruments">The facade bound to exactly one project.</param>
    /// <param name="treeId">The tree identity inside the bound project.</param>
    /// <param name="heading">The label identifying the displayed state.</param>
    /// <param name="details">Whether to include the maintained coordinates and identities.</param>
    /// <param name="cancellationToken">The token for reads and console output.</param>
    /// <returns>A task that completes when the tree has been printed.</returns>
    public static async Task PrintTreeAsync(
        ScopedNestedSet<Instrument, Guid> instruments,
        Guid treeId,
        string heading,
        bool details,
        CancellationToken cancellationToken
    )
    {
        await SampleConsole.HeadingAsync(heading, cancellationToken);

        var nodes = await instruments
            .InTree(treeId)
            .Nodes
            .ToListAsync(cancellationToken);

        foreach (var instrument in nodes)
        {
            var measurement = instrument.PulseCount is { } count ? $" = {count}" : string.Empty;
            var structure = details
                ? $" [Id={instrument.NodeId}, TreeId={instrument.TreeId}, Depth={instrument.Level}, "
                + $"Position={instrument.SiblingPosition}, Bounds={instrument.Start}..{instrument.End}]"
                : string.Empty;

            await SampleConsole.WriteLineAsync(
                $"{new string(' ', instrument.Level * 2)}{instrument.Title}{measurement}{structure}",
                cancellationToken);
        }
    }

    /// <summary>Inspects persisted results without migrating, seeding or changing the database.</summary>
    /// <param name="context">The inspection context, opened read-only when using SQLite.</param>
    /// <param name="details">Whether to include structural details.</param>
    /// <param name="cancellationToken">The token for database reads and output.</param>
    /// <returns>A task that completes after every retained station tree has been printed.</returns>
    public static async Task InspectAsync(
        SignalLabContext context,
        bool details,
        CancellationToken cancellationToken
    )
    {
        var roots = await context
            .Instruments
            .AsNoTracking()
            .Where(instrument => instrument.ParentInstrumentId == null)
            .OrderBy(instrument => instrument.Title)
            .Select(instrument => new
            {
                instrument.ProjectId,
                instrument.TreeId,
                instrument.Title,
            })
            .ToListAsync(cancellationToken);

        foreach (var root in roots)
        {
            var project = root.ProjectId == InstrumentDataset.ProjectId ? "main project" : "other project";
            var instruments = context
                .NestedSet<Instrument>()
                .ForScope(root.ProjectId);

            await PrintTreeAsync(instruments, root.TreeId, $"{root.Title} ({project})", details, cancellationToken);
        }

        // WHY: Inspection never upgrades an older retained database. Its original instrument results remain readable.
        var reportingAvailable = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Any(migration =>
            migration.EndsWith("_SignalAcquisition", StringComparison.Ordinal));

        if (!reportingAvailable)
        {
            await SampleConsole.WriteLineAsync(
                "No acquisition migration is applied; existing instrument results were inspected without schema changes.",
                cancellationToken);

            return;
        }

        var reportingRoots = await context
            .Nodes
            .AsNoTracking()
            // WHY: EF translates this shadow-parent predicate before materialization.
            // ReSharper disable once EntityFramework.ClientSideDbFunctionCall
            .Where(node => EF.Property<Guid?>(node, SignalStorage.ParentId) == null)
            .OrderBy(node => node.Name)
            .Select(node => new
            {
                node.TenantId,
                // WHY: EF projects the shadow tree identity directly from the database.
                // ReSharper disable once EntityFramework.ClientSideDbFunctionCall
                TreeId = EF.Property<Guid>(node, SignalStorage.TreeId),
                node.Name,
            })
            .ToArrayAsync(cancellationToken);

        foreach (var root in reportingRoots)
        {
            var identity = details ? $" (tenant {root.TenantId}, tree {root.TreeId})" : string.Empty;
            await SampleConsole.HeadingAsync(root.Name + identity, cancellationToken);
            var rows = await context
                .NestedSet<SignalNode>()
                .ForScope(root.TenantId)
                .InTree(root.TreeId)
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

            foreach (var row in rows)
            {
                var structure = details ? $" [Id={row.Id}, Bounds={row.Left}..{row.Right}]" : string.Empty;
                await SampleConsole.WriteLineAsync(
                    $"{new string(' ', row.Depth * 2)}{row.Name}{structure}",
                    cancellationToken);
            }
        }

        if (roots.Count == 0
            && reportingRoots.Length == 0)
        {
            await SampleConsole.WriteLineAsync(
                "The SignalLab sample has no persisted hierarchy results.",
                cancellationToken);
        }
    }

    /// <summary>Validates all trees belonging to a scenario, including its two isolation examples.</summary>
    /// <param name="context">The scenario-owned context.</param>
    /// <param name="data">The persisted scenario identities.</param>
    /// <param name="cancellationToken">The token for full validation.</param>
    /// <returns>A task that completes after all trees satisfy the maintained invariants.</returns>
    /// <exception cref="InvalidOperationException">A tree violates its structural contract.</exception>
    public static async Task ValidateAsync(
        SignalLabContext context,
        InstrumentDataset data,
        CancellationToken cancellationToken
    )
    {
        var trees = new[]
        {
            (ProjectId: InstrumentDataset.ProjectId, data.TreeId),
            (ProjectId: InstrumentDataset.ProjectId, TreeId: data.RelatedTreeId),
            (ProjectId: InstrumentDataset.OtherProjectId, data.TreeId),
        };

        foreach (var (projectId, treeId) in trees)
        {
            var report = await context
                .NestedSet<Instrument>()
                .ForScope(projectId)
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, cancellationToken);

            SampleConsole.Require(
                report.IsValid,
                $"Tree {treeId} in project {projectId} failed validation: "
                + string.Join("; ", report.Issues.Select(issue => issue.Message)));
        }
    }
}
