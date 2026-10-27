namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class HierarchyCompositionModelTests
{
    /// <summary>
    /// Upward entity pairs preserve owned auto-includes and repeated ancestors from distinct sources.
    /// </summary>
    /// <param name="includeSelf">Whether each visible source also contributes its self occurrence.</param>
    /// <param name="selection">Visible sources, a hidden selected leaf, or an absent requested source.</param>
    [Theory]
    [InlineData(false, "visible")]
    [InlineData(true, "visible")]
    [InlineData(false, "hidden-leaf")]
    [InlineData(true, "hidden-leaf")]
    [InlineData(false, "missing-leaf")]
    [InlineData(true, "missing-leaf")]
    public async Task AncestorEntityPairsPreserveOwnedAutoIncludes(bool includeSelf, string selection)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await CreateNavigationContextAsync(scope, tree, selection, probe,
            _ => materialized++);

        materialized = 0;
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var minimumSource = selection == "missing-leaf" ? 99 : 2;
        var expected = AncestorNavigationPairs(includeSelf, selection);

        // Act
        var pairs = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(source => source.Id >= minimumSource,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance })
            : hierarchy.SelectAncestors(source => source.Id >= minimumSource,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance });

        // WHY: Both mapped endpoints retain their owned collections in one statement; application split-query
        // defaults must not change the command contract or hide collection identifier loss behind extra queries.
        var actual = await pairs.AsSingleQuery().AsNoTracking()
            .OrderBy(row => row.Source.Id).ThenBy(row => row.Ancestor.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(row => (row.Source.Id, row.Ancestor.Id, row.Distance)));
        Assert.All(actual, row =>
        {
            Assert.Equal($"details-{row.Source.Id}", row.Source.Details.Caption);
            Assert.Equal($"details-{row.Ancestor.Id}", row.Ancestor.Details.Caption);
            Assert.Equal(new[] { $"tag-{row.Source.Id}-1", $"tag-{row.Source.Id}-2" },
                row.Source.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value));
            Assert.Equal(new[] { $"tag-{row.Ancestor.Id}-1", $"tag-{row.Ancestor.Id}-2" },
                row.Ancestor.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value));
        });

        Assert.Equal(actual.Length != 0, materialized != 0);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Upward scalar DTOs retain both endpoints' owned and ordinary collection occurrence bags.</summary>
    /// <param name="includeSelf">Whether the projected path includes each source itself.</param>
    /// <param name="selection">Visible sources, a hidden selected leaf, or an absent requested source.</param>
    [Theory]
    [InlineData(false, "visible")]
    [InlineData(true, "visible")]
    [InlineData(false, "hidden-leaf")]
    [InlineData(true, "hidden-leaf")]
    [InlineData(false, "missing-leaf")]
    [InlineData(true, "missing-leaf")]
    public async Task AncestorProjectionPreservesOwnedAndOrdinaryCollectionDtos(bool includeSelf, string selection)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        var materialized = 0;
        await using var context = await CreateNavigationContextAsync(scope, tree, selection, probe,
            _ => materialized++);

        materialized = 0;
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        var minimumSource = selection == "missing-leaf" ? 99 : 2;
        var expected = AncestorNavigationPairs(includeSelf, selection);

        // Act
        var pairs = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(source => source.Id >= minimumSource,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance })
            : hierarchy.SelectAncestors(source => source.Id >= minimumSource,
                (source, ancestor, distance) => new { Source = source, Ancestor = ancestor, Distance = distance });

        // WHY: The DTO carries collections from both aliases. One SQL reader must reconstruct each source's
        // repeated ancestor occurrence instead of collapsing equal ancestors or multiplying either collection.
        var actual = await pairs.AsSingleQuery().OrderBy(row => row.Source.Id).ThenBy(row => row.Ancestor.Id)
            .Select(row => new
            {
                Source = row.Source.Id,
                Ancestor = row.Ancestor.Id,
                row.Distance,
                SourceCaption = row.Source.Details.Caption,
                AncestorCaption = row.Ancestor.Details.Caption,
                SourceTags = row.Source.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
                SourceItems = row.Source.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
                AncestorTags = row.Ancestor.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
                AncestorItems = row.Ancestor.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
            }).ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(row => (row.Source, row.Ancestor, row.Distance)));
        Assert.All(actual, row =>
        {
            Assert.Equal($"details-{row.Source}", row.SourceCaption);
            Assert.Equal($"details-{row.Ancestor}", row.AncestorCaption);
            Assert.Equal(new[] { $"tag-{row.Source}-1", $"tag-{row.Source}-2" }, row.SourceTags);
            Assert.Equal(new[] { $"item-{row.Source}-1", $"item-{row.Source}-2" }, row.SourceItems);
            Assert.Equal(new[] { $"tag-{row.Ancestor}-1", $"tag-{row.Ancestor}-2" }, row.AncestorTags);
            Assert.Equal(new[] { $"item-{row.Ancestor}-1", $"item-{row.Ancestor}-2" }, row.AncestorItems);
        });

        Assert.Equal(0, materialized);
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Lists source/ancestor occurrences and distances independently of the SQL projection.</summary>
    private static (int Source, int Ancestor, int Distance)[] AncestorNavigationPairs(
        bool includeSelf,
        string selection
    )
        => (includeSelf, selection) switch
        {
            (false, "visible") => [(2, 1, 1), (3, 1, 2), (3, 2, 1)],
            (true, "visible") => [(2, 1, 1), (2, 2, 0), (3, 1, 2), (3, 2, 1), (3, 3, 0)],
            (false, "hidden-leaf") => [(2, 1, 1)],
            (true, "hidden-leaf") => [(2, 1, 1), (2, 2, 0)],
            (_, "missing-leaf") => [],
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
}
