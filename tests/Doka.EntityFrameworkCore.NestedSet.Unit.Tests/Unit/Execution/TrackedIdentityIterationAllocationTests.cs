namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Protects allocation-free traversal of stable captured guard identities.</summary>
// WHY: Keep this fixed-budget allocation check in the existing nonparallel measurement collection.
// Unrelated test work must not compete with its warmup and measurement window.
[Collection("Allocation measurements")]
public sealed class TrackedIdentityIterationAllocationTests
{
    /// <summary>Repeated late checks do not allocate an iterator for the captured candidate blocks.</summary>
    /// <param name="candidates">The empty, singleton, and block-boundary candidate counts.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void VerifyUnchangedAvoidsPerTraversalAllocation(
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
        context.AttachRange(Enumerable.Range(1, candidates).Select(id => new TreeNode
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
                [new NestedSetTreeLockRequest<Guid, int>(
                    metadata,
                    scope,
                    Guid.NewGuid(),
                    NestedSetTreeLockMode.Existing)])!;

        const int repetitions = 1000;

        for (var index = 0; index < repetitions; index++)
        {
            guard.VerifyUnchanged();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        for (var index = 0; index < repetitions; index++)
        {
            guard.VerifyUnchanged();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        // WHY: A fixed allowance tolerates runtime noise while rejecting the former 80-byte iterator on every check.
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(candidates != 0, guard.HasCandidates);
        Assert.Equal(candidates, context.ChangeTracker.Entries<TreeNode>().Count());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
