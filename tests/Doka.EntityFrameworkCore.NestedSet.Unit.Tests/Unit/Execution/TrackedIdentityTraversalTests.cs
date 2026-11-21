namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Preserves captured guard identities across empty and block-boundary candidate counts.</summary>

public sealed class TrackedIdentityTraversalTests
{
    /// <summary>Late checks preserve the complete captured candidate blocks.</summary>
    /// <param name="candidates">The empty, singleton, and block-boundary candidate counts.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void VerifyUnchanged_StableCandidates_RetainTheirCapturedState(
        int candidates
    )
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .Options;

        using var context = new TreeContext(options);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var metadata = context.Model.FindEntityType(typeof(TreeNode))!;
        var map = NestedSetMapping<TreeNode, int, int>.For(context, metadata);
        const int scope = 7;
        context.AttachRange(
            Enumerable
                .Range(1, candidates)
                .Select(id => new TreeNode
                {
                    NodeId = id,
                    Tree = scope,
                    TreeId = Guid.NewGuid(),
                    Start = 1,
                    End = 2,
                }));

        using var guard =
            NestedSetTrackedIdentityGuard<TreeNode, int, Guid, int>.Capture(
                context,
                map,
                [
                    new NestedSetTreeLockRequest<Guid, int>(
                        metadata,
                        scope,
                        Guid.NewGuid(),
                        NestedSetTreeLockMode.Existing),
                ])!;

        // Act
        guard.VerifyUnchanged();

        // Assert
        Assert.Equal(candidates != 0, guard.HasCandidates);
        Assert.Equal(
            candidates,
            context
                .ChangeTracker
                .Entries<TreeNode>()
                .Count());

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
