namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class AncestorIndexQueryTests
{
    /// <summary>Late mapped entity joins retain both endpoints' owned and ordinary collection identifiers.</summary>
    /// <param name="includeSelf">Whether leaf owners participate alongside indexed nonleaf owners.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedAncestorsPreserveOwnedAndOrdinaryCollections(bool includeSelf)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe);
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        int[] expected = includeSelf ? [1, 2, 3] : [1, 2];
        string[] sourceTags = ["tag-3-1", "tag-3-2"];
        string[] sourceItems = ["item-3-1", "item-3-2"];
        var pairs = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3,
                (source, owner, distance) => new { Source = source, Owner = owner, Distance = distance })
            : hierarchy.SelectAncestors(node => node.Id == 3,
                (source, owner, distance) => new { Source = source, Owner = owner, Distance = distance });

        var query = pairs.AsSingleQuery().OrderBy(pair => pair.Owner.Id).Select(pair => new
        {
            Source = pair.Source.Id,
            Owner = pair.Owner.Id,
            pair.Distance,
            pair.Owner.Details.Caption,
            SourceTags = pair.Source.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
            SourceItems = pair.Source.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
            OwnerTags = pair.Owner.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value).ToArray(),
            OwnerItems = pair.Owner.Items.OrderBy(item => item.Id).Select(item => item.Label).ToArray(),
        });

        probe.Reset();

        // Act
        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual.Select(row => row.Owner));
        Assert.All(actual, row =>
        {
            Assert.Equal(3, row.Source);
            Assert.Equal(3 - row.Owner, row.Distance);
            Assert.Equal($"details-{row.Owner}", row.Caption);
            Assert.Equal(sourceTags, row.SourceTags);
            Assert.Equal(sourceItems, row.SourceItems);
            Assert.Equal(new[] { $"tag-{row.Owner}-1", $"tag-{row.Owner}-2" }, row.OwnerTags);
            Assert.Equal(new[] { $"item-{row.Owner}-1", $"item-{row.Owner}-2" }, row.OwnerItems);
        });

        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Projecting a mapped owner keeps its automatically included owned reference and collection.</summary>
    /// <param name="includeSelf">Whether leaf owners are included.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedAncestorsPreserveMappedEntityPayload(bool includeSelf)
    {
        // Arrange
        var scope = Guid.NewGuid();
        var tree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await CreateContextAsync(scope, tree, probe);
        var hierarchy = context.NestedSet<CompositionNavigationNode>().ForScope(scope).InTree(tree);
        int[] expected = includeSelf ? [1, 2, 3] : [1, 2];
        var query = includeSelf
            ? hierarchy.SelectAncestorsOrSelf(node => node.Id == 3, (source, owner, distance) => owner)
            : hierarchy.SelectAncestors(node => node.Id == 3, (source, owner, distance) => owner);

        probe.Reset();

        // Act
        var actual = await query.AsSingleQuery().OrderBy(owner => owner.Id).ToArrayAsync(CancellationToken.None);

        // Assert
        AssertOptionalSourceStrategy(context, query, includeSelf);
        Assert.Equal(expected, actual.Select(owner => owner.Id));
        Assert.All(actual, owner =>
        {
            Assert.Equal($"details-{owner.Id}", owner.Details.Caption);
            Assert.Equal(new[] { $"tag-{owner.Id}-1", $"tag-{owner.Id}-2" },
                owner.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Value));
        });

        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
