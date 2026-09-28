namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies full contract-fixture resets remove model-specific tree lifecycle state.</summary>
public abstract class FixtureTreeLifecycleTests : ProviderTest
{
    private readonly ContractFixture _fixture;

    /// <summary>Creates lifecycle probes using independently reset contract models.</summary>
    /// <param name="fixture">The database owner for configured-access and generated-key models.</param>
    protected FixtureTreeLifecycleTests(
        IProviderFixture<ContractFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A complete field-model reset permits a fresh reservation of a previously retired identity.</summary>
    /// <returns>A task that completes after verifying the replacement tree is valid.</returns>
    [Fact]
    public async Task FieldModelResetRemovesTombstonedIdentity()
    {
        // Arrange
        await using (var previous = await _fixture.CreateFieldContextAsync(Engine))
        {
            var hierarchy = previous
                .NestedSet<FieldNode>()
                .ForScope(1);

            await hierarchy.InsertRootAsync(new FieldNode { Id = 1 }, Guid.Empty, CancellationToken.None);
            await hierarchy.DeleteSubtreeAsync(1, CancellationToken.None);
        }

        // Act
        await using var context = await _fixture.CreateFieldContextAsync(Engine);
        var replacement = context
            .NestedSet<FieldNode>()
            .ForScope(1);
        await replacement.InsertRootAsync(new FieldNode { Id = 2 }, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(
            1,
            await replacement
                .InTree(Guid.Empty)
                .Nodes
                .CountAsync(CancellationToken.None));
        Assert.True(
            (await replacement
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>A complete generated-model reset removes lifecycle state independently of the base model.</summary>
    /// <returns>A task that completes after verifying identity generation and fresh tree reservation.</returns>
    [Fact]
    public async Task GeneratedModelResetRemovesTombstonedIdentity()
    {
        // Arrange
        await using (var previous = await _fixture.CreateGeneratedContextAsync(Engine))
        {
            var hierarchy = previous
                .NestedSet<TreeNode>()
                .ForScope(1);

            var root = new TreeNode();
            await hierarchy.InsertRootAsync(root, Guid.Empty, CancellationToken.None);
            await hierarchy.DeleteSubtreeAsync(root.NodeId, CancellationToken.None);
        }

        // Act
        await using var context = await _fixture.CreateGeneratedContextAsync(Engine);
        var replacement = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var node = new TreeNode();
        await replacement.InsertRootAsync(node, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.True(node.NodeId > 0);
        Assert.Equal((1L, 2L, 0, 0L), (node.Start, node.End, node.Depth, node.Position));
        Assert.True(
            (await replacement
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }
}
