namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionTests
{
    /// <summary>Captures parameter-bound provider plans for the earliest composition translation seams.</summary>
    /// <param name="shape">The generic composition whose executed command is explained.</param>
    [Theory]
    [InlineData("Path")]
    [InlineData("Nearest")]
    [InlineData("UpDown")]
    public async Task CompositionRetainsExecutedSqlAndNativePlanEvidence(
        string shape
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var planProbe = new EnterpriseProbe(captureParameterBindings: true);
        await using var context = await CreateContextAsync(scope, probe, planProbe: planProbe);
        var hierarchy = context.NestedSet<CompositionNode>().ForScope(scope);
        var commandIndex = planProbe.Commands.Count;
        IQueryable<int> query;

        if (shape == "Path")
        {
            var pairs = hierarchy.SelectSubtrees(
                anchor => anchor.Id == 1,
                (anchor, node, distance) => new { Anchor = anchor, Node = node });

            query = hierarchy.WherePathEdges(
                    pairs,
                    row => row.Anchor,
                    row => row.Node,
                    (row, parent, child) => child.EdgeOpen)
                .Select(row => row.Node.Id);
        }
        else if (shape == "Nearest")
        {
            var source = context.Set<CompositionNode>()
                .Where(node => node.Scope == scope && node.Id == 3)
                .Select(node => new { Node = node });

            query = hierarchy.WhereNearestAncestorOrSelf(
                    source,
                    row => row.Node,
                    (row, owner) => owner.Candidate,
                    (row, owner) => owner.Id == 2)
                .Select(row => row.Node.Id);
        }
        else
        {
            var up = hierarchy.SelectAncestors(
                source => source.Id == 3,
                (source, pivot, distance) => new { Source = source, Pivot = pivot });

            var down = hierarchy.SelectSubtrees(
                pivot => up.Any(row => row.Pivot == pivot),
                (pivot, target, distance) => new { Pivot = pivot, Target = target });

            query = up.Join(
                down,
                row => row.Pivot,
                row => row.Pivot,
                (first, second) => second.Target.Id);
        }

        // Act
        var rows = await query.ToArrayAsync(CancellationToken.None);
        var queryCommands = probe.CommandCount;
        var plan = await QueryPlanTestSupport.ExplainAsync(context, planProbe, commandIndex, Engine);
        await QueryPlanTestSupport.WriteEvidenceAsync(
            Engine + "-composition-" + shape,
            [planProbe.Commands[commandIndex], plan]);

        // Assert
        Assert.NotEmpty(rows);
        Assert.Equal(1, queryCommands);
        Assert.False(string.IsNullOrWhiteSpace(plan));
    }
}
