namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>Retains exact visible structural ancestors when matching and rehydrating sparse key pairs.</summary>
    /// <param name="shape">The visible, hidden, missing, or adjacency-inconsistent selected endpoint.</param>
    [Theory]
    [InlineData("visible")]
    [InlineData("hidden-source")]
    [InlineData("hidden-owner")]
    [InlineData("missing-source")]
    [InlineData("parent-mismatch")]
    public async Task SparseAncestorPairsKeepScopeTreeVisibilityAndBoundsSemantics(string shape)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var otherScope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var otherTree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateNavigationContextAsync(scope, tree, "visible", probe, _ => { });

        // WHY: Equal keys in another scope and equal bounds in another authorized tree independently
        // challenge both sides of the structural-key rehydration; coordinates alone never identify an owner.
        foreach (var foreignScope in new[] { false, true })
        {
            for (var depth = 0; depth < 3; depth++)
            {
                var offset = foreignScope ? 0 : 10;
                await context.AddAsync(new CompositionNavigationNode
                {
                    Scope = foreignScope ? otherScope : scope,
                    TreeId = otherTree,
                    Id = offset + depth + 1,
                    ParentId = depth == 0 ? null : offset + depth,
                    Left = depth + 1,
                    Right = 6 - depth,
                    Depth = depth,
                    Visible = true,
                    Details = new CompositionNavigationDetail { Caption = "unrelated" },
                }, CancellationToken.None);
            }
        }

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        if (shape is "hidden-source" or "hidden-owner")
        {
            var hidden = shape == "hidden-source" ? 3 : 2;
            await context.Set<CompositionNavigationNode>().Where(node => node.Scope == scope && node.Id == hidden)
                .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.Visible, false),
                    CancellationToken.None);
        }

        if (shape == "parent-mismatch")
        {
            // WHY: Ancestor expansion is a bounds contract. Only WherePathEdges may reject an incomplete
            // direct-parent path; changing this relation to walk adjacency would silently change its results.
            await context.Set<CompositionNavigationNode>().Where(node => node.Scope == scope && node.Id == 3)
                .ExecuteUpdateAsync(setters => setters.SetProperty(node => node.ParentId, (int?)null),
                    CancellationToken.None);
        }

        probe.Reset();
        var source = shape == "missing-source" ? 99 : 3;
        int[] expected = shape switch
        {
            "hidden-source" or "missing-source" => [],
            "hidden-owner" => [1],
            _ => [1, 2],
        };

        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope);

        // Act
        var actual = await hierarchy.SelectAncestors(node => node.Id == source,
                (node, ancestor, distance) => new
                {
                    Source = node.Id, Owner = ancestor.Id, Scope = ancestor.Scope,
                    Tree = ancestor.TreeId, Distance = distance, Caption = ancestor.Details.Caption,
                    Tags = ancestor.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
                })
            .AsSingleQuery().OrderBy(row => row.Owner).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(row => row.Owner));
        Assert.All(actual, row =>
        {
            Assert.Equal(3, row.Source);
            Assert.Equal(scope, row.Scope);
            Assert.Equal(tree, row.Tree);
            Assert.Equal(3 - row.Owner, row.Distance);
            Assert.Equal($"details-{row.Owner}", row.Caption);
            Assert.Equal(new[] { $"tag-{row.Owner}-1", $"tag-{row.Owner}-2" }, row.Tags);
        });

        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
