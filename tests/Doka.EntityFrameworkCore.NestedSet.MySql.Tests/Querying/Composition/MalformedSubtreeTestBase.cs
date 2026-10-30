namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

/// <summary>Qualifies Doka subtree nonleaf gates without changing other providers' malformed-self semantics.</summary>
public abstract class MalformedSubtreeTestBase : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Uses the exact provider fixture owning this suite's cached model database.</summary>
    /// <param name="fixture">The collection-owned MySQL or MariaDB model fixture.</param>
    protected MalformedSubtreeTestBase(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Inclusive subtrees keep self by identity and apply strict bounds without overflowing malformed anchors.</summary>
    [Fact]
    public async Task SplitSubtreesRetainIdentitySelfAndStrictMalformedContainment()
    {
        // Arrange
        var first = MalformedIntervalTestSupport.NextIdentity();
        var tree = Guid.NewGuid();
        var foreignTree = Guid.NewGuid();
        var probe = new CompositionCommandProbe();
        await using var context = await _fixture.CreateContextAsync<AncestorIndexMappedContext>(
            Engine,
            static options => new AncestorIndexMappedContext(options),
            probe);

        var bounds = MalformedIntervalTestSupport.Bounds();
        var nodes = await MalformedIntervalTestSupport.SeedAsync(context, first, tree, foreignTree, bounds);
        var query = context
            .NestedSet<EntitySplitNode>()
            .SelectSubtrees(
                source => source.Id >= first && source.Id < first + bounds.Length,
                (source, related, distance) => new
                {
                    Source = source.Id,
                    Related = related.Id,
                });

        // WHY: Self is an identity contract of SelectSubtrees, separate from its strict descendant
        // relation. The independent four-comparison oracle keeps that distinction for malformed rows.
        var expected = nodes
            .Take(bounds.Length)
            .SelectMany(source => nodes
                .Where(related => related.TreeId == source.TreeId
                    && (related.Id == source.Id
                        || (source.Left < related.Left
                            && source.Right > related.Right
                            && related.Left < source.Right
                            && related.Right > source.Left)))
                .Select(related => (Source: source.Id, Related: related.Id)))
            .OrderBy(pair => pair.Source)
            .ThenBy(pair => pair.Related)
            .ToArray();

        probe.Reset();

        // Act
        var actual = await query
            .OrderBy(pair => pair.Source)
            .ThenBy(pair => pair.Related)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, actual.Select(pair => (pair.Source, pair.Related)));
        Assert.Equal(1, probe.CommandCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
