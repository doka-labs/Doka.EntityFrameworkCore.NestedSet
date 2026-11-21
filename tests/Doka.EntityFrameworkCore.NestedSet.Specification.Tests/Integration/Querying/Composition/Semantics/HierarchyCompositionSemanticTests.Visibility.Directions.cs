namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionSemanticTests
{
    /// <summary>Visibility gaps retain raw, existential, and complete-path semantics across a turn.</summary>
    /// <param name="filter">The explicit policy applied to the upward leg of the composed walk.</param>
    /// <param name="accepted">Whether the policy permits visible endpoints across the hidden middle.</param>
    [Theory]
    [InlineData("Raw", true)]
    [InlineData("Any", true)]
    [InlineData("Path", false)]
    [InlineData("Nearest", false)]
    public async Task WhereAnyAncestorOrSelf_DirectionChange_PreservesExplicitHiddenPathPolicy(
        string filter,
        bool accepted
    )
    {
        // Arrange
        var scope = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(
            scope,
            probe,
            hidden: 2,
            cancellationToken: TestContext.Current.CancellationToken,
            sql: null,
            chainNodes: null);

        var hierarchy = context
            .NestedSet<CompositionNode>()
            .ForScope(scope);

        var up = hierarchy
            .SelectAncestors(
                source => source.Id == 3,
                (source, pivot, distance) => new
                {
                    Source = source,
                    Pivot = pivot,
                    UpDistance = distance,
                })
            .Where(row => row.Pivot.Id == 1);

        var down = hierarchy
            .SelectSubtrees(
                pivot => up.Any(row => row.Pivot == pivot),
                (pivot, target, distance) => new
                {
                    Pivot = pivot,
                    Target = target,
                    DownDistance = distance,
                })
            .Where(row => row.Target.Id == 6);

        var walks = up.Join(
            down,
            row => row.Pivot,
            row => row.Pivot,
            (first, second) => new
            {
                first.Source,
                first.Pivot,
                second.Target,
                first.UpDistance,
                second.DownDistance,
            });

        // Act
        var query = filter switch
        {
            "Any" => hierarchy.WhereAnyAncestorOrSelf(walks, row => row.Source, (row, owner) => owner.Id == 1),
            "Path" => hierarchy.WherePathEdges(
                walks,
                row => row.Pivot,
                row => row.Source,
                (row, parent, child) => true),
            "Nearest" => hierarchy.WhereNearestAncestorOrSelf(
                walks,
                row => row.Source,
                (row, owner) => owner.Id == 1,
                (row, owner) => true),
            _ => walks,
        };

        var actual = await query
            .Select(row => new
            {
                Source = row.Source.Id,
                Pivot = row.Pivot.Id,
                Target = row.Target.Id,
                row.UpDistance,
                row.DownDistance,
            })
            .ToArrayAsync(CancellationToken.None);

        // Assert
        (int Source, int Pivot, int Target, int Up, int Down)[] expected = accepted
            ? [(3, 1, 6, 2, 2)]
            : [];

        Assert.Equal(
            expected,
            actual.Select(row => (row.Source, row.Pivot, row.Target, row.UpDistance, row.DownDistance)));

        Assert.Equal(1, probe.CommandCount);
        Assert.Equal(0, probe.MaterializedNodes);
    }
}
