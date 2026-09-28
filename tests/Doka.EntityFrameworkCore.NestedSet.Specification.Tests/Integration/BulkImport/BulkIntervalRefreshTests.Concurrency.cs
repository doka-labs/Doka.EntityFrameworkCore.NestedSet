namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class BulkIntervalRefreshTests
{
    /// <summary>The token refreshed after generated parent repair permits a subsequent ordinary tracked save.</summary>
    [Fact]
    public async Task RefreshedGeneratedTokenAllowsOrdinaryPayloadSave()
    {
        // Arrange
        await using var context = await _generated.ResetAsync(Engine);
        var tree = context
            .NestedSet<BulkIntervalNode>()
            .ForScope(1);

        var nodes = Enumerable
            .Range(0, NodeCount)
            .Select(_ => new BulkIntervalNode())
            .ToArray();

        var branch = new NestedSetBranch<BulkIntervalNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<BulkIntervalNode>(node))
                .ToArray());

        await tree.InsertForestAsync(
            [new NestedSetTreeImport<BulkIntervalNode, Guid>(Guid.Empty, branch),],
            CancellationToken.None);

        var leaf = nodes[^1];
        context.Attach(leaf);
        leaf.Payload = "After import";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            "After import",
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .Where(node => node.Id == leaf.Id)
                .Select(node => node.Payload)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(
            leaf.Version,
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .Where(node => node.Id == leaf.Id)
                .Select(node => node.Version)
                .SingleAsync(CancellationToken.None));
        Assert.Equal(leaf.Version + 1000, leaf.Details.Revision);
    }
}
