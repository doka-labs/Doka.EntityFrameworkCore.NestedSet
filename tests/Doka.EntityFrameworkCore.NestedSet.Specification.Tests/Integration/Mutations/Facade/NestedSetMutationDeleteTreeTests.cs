namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies whole-tree deletion with restrictive application foreign keys.</summary>
public abstract class NestedSetMutationDeleteTreeTests : ProviderTest
{
    private readonly RelationalFixture _fixture;

    /// <summary>Creates a deletion test over the established provider fixture.</summary>
    protected NestedSetMutationDeleteTreeTests(
        IProviderFixture<RelationalFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Whole-tree deletion satisfies restrictive self references on every supported provider.</summary>
    [Fact]
    public async Task DeleteTreeSupportsRestrictiveSelfForeignKey()
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var context = database.CreateContext();
        var hierarchy = context
            .NestedSet<ConstrainedNode>()
            .ForScope(1);

        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(new ConstrainedNode { Id = 1 }, treeId, CancellationToken.None);
        await hierarchy.InsertChildAsync(new ConstrainedNode { Id = 2 }, 1, CancellationToken.None);
        await hierarchy.InsertChildAsync(new ConstrainedNode { Id = 3 }, 2, CancellationToken.None);

        // Act
        await hierarchy.DeleteTreeAsync(treeId, CancellationToken.None);

        // Assert
        Assert.Empty(
            await context
                .Set<ConstrainedNode>()
                .ToArrayAsync(CancellationToken.None));
    }
}
