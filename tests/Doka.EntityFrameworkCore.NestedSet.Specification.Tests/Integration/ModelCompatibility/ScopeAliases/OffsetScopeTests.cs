namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that save planning never merges distinct provider scope representations.</summary>
[Collection("Model compatibility")]
public abstract class OffsetScopeTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares provider databases with the other mapping-compatibility tests.</summary>
    protected OffsetScopeTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Reorders both converted offsets while retaining their separate registry identities.</summary>
    [Fact]
    public async Task SameInstantScopesRemainIndependentDuringSaveAsync()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<OffsetScopeContext>(
            Engine,
            static options => new OffsetScopeContext(options));

        var firstScope = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var secondScope = firstScope.ToOffset(TimeSpan.FromHours(2));
        var firstTree = Guid.NewGuid();
        var secondTree = Guid.NewGuid();
        Seed(context, firstScope, firstTree);
        Seed(context, secondScope, secondTree);
        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        var tracked = await context
            .Set<OffsetScopeNode>()
            .ToArrayAsync(CancellationToken.None);

        tracked.Single(node => node.Scope.Offset == firstScope.Offset && node.Id == 2).Name = "Zulu";
        tracked.Single(node => node.Scope.Offset == secondScope.Offset && node.Id == 2).Name = "Zulu";

        // Act
        await context.SaveNestedSetChangesAsync(
            true,
            token => context.SaveChangesAsync(false, token),
            CancellationToken.None);

        // Assert
        foreach (var scope in new[] { firstScope, secondScope })
        {
            var children = tracked
                .Where(node => node.Scope.Offset == scope.Offset && node.Depth == 1)
                .ToArray();

            Assert.Equal((4L, 5L, 1L), Bounds(children.Single(node => node.Id == 2)));
            Assert.Equal((2L, 3L, 0L), Bounds(children.Single(node => node.Id == 3)));
        }

        Assert.True(
            (await context
                .NestedSet<OffsetScopeNode>()
                .ForScope(firstScope)
                .InTree(firstTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        Assert.True(
            (await context
                .NestedSet<OffsetScopeNode>()
                .ForScope(secondScope)
                .InTree(secondTree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Seeds one precomputed tree for the converted scope.</summary>
    private static void Seed(
        DbContext context,
        DateTimeOffset scope,
        Guid treeId
    )
    {
        context.AddRange(
            new OffsetScopeNode
            {
                Scope = scope,
                Id = 1,
                TreeId = treeId,
                Name = "Root",
                Left = 1,
                Right = 6,
            },
            new OffsetScopeNode
            {
                Scope = scope,
                Id = 2,
                TreeId = treeId,
                ParentId = 1,
                Name = "Alpha",
                Left = 2,
                Right = 3,
                Depth = 1,
            },
            new OffsetScopeNode
            {
                Scope = scope,
                Id = 3,
                TreeId = treeId,
                ParentId = 1,
                Name = "Bravo",
                Left = 4,
                Right = 5,
                Depth = 1,
                Position = 1,
            });
    }

    /// <summary>Returns the structural coordinates relevant to sibling ordering.</summary>
    private static (long Left, long Right, long Position) Bounds(
        OffsetScopeNode node
    ) => (node.Left, node.Right, node.Position);
}
