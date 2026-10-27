using ChainOwnerMode = Doka.EntityFrameworkCore.NestedSet.Tests.CompositionTestData.ChainOwnerMode;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>Retains selective deep-path and dense-owner evidence before enabling larger feature profiles.</summary>
    /// <param name="nodes">The persisted chain length, including its root.</param>
    /// <param name="shape">The path, dense rejection, dense acceptance, or root-owner scenario.</param>
    [Theory]
    [InlineData(1_000, "Path")]
    [InlineData(10_000, "Path")]
    [InlineData(1_000, "NearestReject")]
    [InlineData(10_000, "NearestReject")]
    [InlineData(1_000, "NearestAccept")]
    [InlineData(10_000, "NearestAccept")]
    [InlineData(1_000, "RootOwner")]
    [InlineData(10_000, "RootOwner")]
    [Trait("Category", "Capacity")]
    public async Task SelectiveDeepCompositionHasBoundedResultsAndInspectablePlans(
        int nodes,
        string shape
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        var setupStarted = Stopwatch.GetTimestamp();
        var ownerMode = shape switch
        {
            "NearestAccept" => ChainOwnerMode.AllValues,
            "RootOwner" => ChainOwnerMode.RootOnly,
            _ => ChainOwnerMode.RootValue,
        };

        await using var context = await CreateContextAsync(
            scope,
            probe,
            planProbe: planProbe,
            chainNodes: nodes,
            ownerMode: ownerMode);

        await QueryPlanTestSupport.RefreshStatisticsAsync<CompositionNode>(context, Engine);
        var setupElapsed = Stopwatch.GetElapsedTime(setupStarted);
        probe.Reset();
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var parents = Enumerable.Range(1, nodes)
            .ToDictionary(key => key, key => key == 1 ? null : (int?)(key - 1));

        var expectedDistance = DistanceFromParents(parents, nodes, 1);
        var commandIndex = planProbe.Commands.Count;
        IQueryable<int> query;

        if (shape != "Path")
        {
            var source = context.Set<CompositionNode>()
                .Where(node => node.Scope == scope && node.Id == nodes)
                .Select(node => new { Node = node });

            // WHY: Separate dense acceptance from rejection: a selective failing value must not hide expensive
            // repeated selection when every owner passes. A root-only candidate also exercises a full selected path.
            query = hierarchy.WhereNearestAncestorOrSelf(
                    source,
                    row => row.Node,
                    (row, owner) => owner.Candidate,
                    (row, owner) => owner.Matches)
                .Select(row => row.Node.Id);
        }
        else
        {
            var pairs = hierarchy.SelectSubtrees(
                    anchor => anchor.Id == 1,
                    (anchor, node, distance) => new { Anchor = anchor, Node = node, Distance = distance })
                .Where(row => row.Node.Id == nodes);

            query = hierarchy.WherePathEdges(
                    pairs,
                    row => row.Anchor,
                    row => row.Node,
                    (row, parent, child) => child.EdgeOpen)
                .Select(row => row.Distance);
        }

        var commandsBeforeExecution = probe.CommandCount;

        // Act
        var started = Stopwatch.GetTimestamp();
        var actual = await query.ToArrayAsync(CancellationToken.None);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var queryCommands = probe.CommandCount;
        var materializedNodes = probe.MaterializedNodes;
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, commandIndex, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-composition-deep-" + shape + "-" + nodes,
            [
                FormattableString.Invariant(
                    $"Nodes={nodes}; elapsed={elapsed.TotalMilliseconds:F3}ms; commands={queryCommands}"),
                FormattableString.Invariant($"setup={setupElapsed.TotalMilliseconds:F3}ms"),
                $"rows={actual.Length}; materialized={materializedNodes}",
                planProbe.Commands[commandIndex],
                plan,
            ]);

        // Assert
        Assert.Equal(0, commandsBeforeExecution);
        Assert.Equal(1, queryCommands);
        Assert.Equal(0, materializedNodes);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.False(string.IsNullOrWhiteSpace(plan));
        int[] expected = shape switch
        {
            "Path" => [expectedDistance],
            "NearestReject" => [],
            _ => [nodes],
        };

        Assert.Equal(expected, actual);
    }

    /// <summary>Calculates expected structural distance using adjacency alone, independently of bounds.</summary>
    private static int DistanceFromParents(
        Dictionary<int, int?> parents,
        int node,
        int anchor
    )
    {
        var distance = 0;

        while (node != anchor)
        {
            node = parents[node] ?? throw new InvalidOperationException("The fixture anchor is not an ancestor.");
            distance++;
        }

        return distance;
    }
}
