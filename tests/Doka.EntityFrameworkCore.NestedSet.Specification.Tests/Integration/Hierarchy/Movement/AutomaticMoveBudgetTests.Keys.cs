namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>
///     Preserves database-native identity semantics when automatic placement bypasses the manual resolver.
/// </summary>
public abstract class AutomaticMoveKeyTests : ProviderTest
{
    private readonly OrderingKeyFixture _fixture;

    /// <summary>Reuses the case-insensitive key model with a deliberately different parent-column collation.</summary>
    protected AutomaticMoveKeyTests(
        IProviderFixture<OrderingKeyFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>
    ///     A key alias resolves to the persisted canonical parent while descendants retain their own links.
    /// </summary>
    [Fact]
    public async Task AutomaticDestinationUsesTheCanonicalParentKey()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = context
            .NestedSet<OrderingKeyNode<string, string?>>()
            .ForScope(1);

        await tree.InsertRootAsync(new OrderingKeyNode<string, string?>("A", "Alpha"), Guid.Empty, CancellationToken.None);
        await tree.InsertRootAsync(new OrderingKeyNode<string, string?>("Z", "Zulu"), Guid.NewGuid(), CancellationToken.None);
        await tree.InsertChildAsync(new OrderingKeyNode<string, string?>("SOURCE", "Middle"), "a", CancellationToken.None);
        await tree.InsertChildAsync(new OrderingKeyNode<string, string?>("LEAF", "Leaf"), "source", CancellationToken.None);

        // Act
        await tree.MoveToAsync("source", "z", CancellationToken.None);

        // Assert
        var rows = await tree
            .SubtreeOf("SOURCE")
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(["SOURCE", "LEAF"], rows.Select(node => node.Id));
        Assert.Equal("Z", rows[0].ParentId);
        Assert.Equal("SOURCE", rows[1].ParentId);
        Assert.Equal([1, 2], rows.Select(node => node.Depth));
        Assert.True(
            (await tree
                .InTree(rows[0].TreeId)
                .ValidateAsync(
                    NestedSetValidationLevel.Full,
                    CancellationToken.None)).IsValid);
    }
}
