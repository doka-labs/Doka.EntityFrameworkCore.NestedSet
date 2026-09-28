namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies that inline owned payload preserves the single-table delete path.</summary>
[Collection("Model compatibility")]
public abstract class OwnedPayloadDeletionTests : ProviderTest
{
    private readonly ModelCompatibilityDatabase _fixture;

    /// <summary>Shares provider databases with the other mapping-compatibility tests.</summary>
    protected OwnedPayloadDeletionTests(
        IProviderFixture<ModelCompatibilityDatabase> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Deletes an owned subtree through EF's one-statement path and keeps the surviving payload.</summary>
    [Fact]
    public async Task InlineOwnedPayloadDoesNotRequirePhysicalBatchDelete()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<PayloadContext>(
            Engine,
            static options => new PayloadContext(options));

        var hierarchy = context.NestedSet<OwnedPayloadNode>();
        var treeId = Guid.NewGuid();
        await hierarchy.InsertRootAsync(
            new OwnedPayloadNode
            {
                Id = 1,
                Details = new OwnedNodeDetails { Label = "Root" },
            },
            treeId,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new OwnedPayloadNode
            {
                Id = 2,
                Details = new OwnedNodeDetails { Label = "Branch" },
            },
            1,
            CancellationToken.None);

        await hierarchy.InsertChildAsync(
            new OwnedPayloadNode
            {
                Id = 3,
                Details = new OwnedNodeDetails { Label = "Leaf" },
            },
            2,
            CancellationToken.None);

        var store = new NestedSetStore<OwnedPayloadNode, int, Guid, NestedSetNoScope>(
            context,
            context.Model.FindEntityType(typeof(OwnedPayloadNode))!,
            default,
            treeId);

        var physicalDelete = new NestedSetPhysicalDelete<OwnedPayloadNode, int, Guid, NestedSetNoScope>(store);

        // Act
        await hierarchy.DeleteSubtreeAsync(2, CancellationToken.None);

        // Assert
        Assert.False(physicalDelete.IsRequired);
        var remaining = await context
            .Set<OwnedPayloadNode>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(1, remaining.Id);
        Assert.Equal("Root", remaining.Details.Label);
        Assert.True(
            (await hierarchy
                .InTree(treeId)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }
}
