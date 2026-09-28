namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Isolates process-wide heap measurements from other unit test collections.</summary>
[CollectionDefinition("BulkPlanAllocation", DisableParallelization = true)]
public sealed class BulkPlanAllocationIsolation;

/// <summary>Verifies the deterministic client-side memory shape of large bulk plans.</summary>
[Collection("BulkPlanAllocation")]
public sealed class BulkPlanAllocationTests
{
    private const int NodeCount = 20_000;

    // WHY: The plan receives only 320 MiB of the 512 MiB total target so native sort ranks, query buffers and the
    // bounded EF batch retain an explicit 192 MiB reserve at the one-million-node qualification size.
    private const long BytesPerNodeBudget = 320L * 1024 * 1024 / 1_000_000;
    private readonly ITestOutputHelper _output;

    /// <summary>Includes measured allocations in the test result for reproducible budget verification.</summary>
    public BulkPlanAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>A wide plan reserves heap for ranks and the active batch inside the full 512 MiB target.</summary>
    [Fact]
    public async Task WidePlanStaysWithinMemoryBudget()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .Options;

        await using var context = new TreeContext(options);
        var store = new NestedSetStore<TreeNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(TreeNode))!,
            1,
            Guid.Empty);

        var children = Enumerable
            .Range(2, NodeCount - 1)
            .Select(id => new NestedSetBranch<TreeNode>(new TreeNode { NodeId = id }))
            .ToArray();

        var root = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);
        _ = context.Model;

        _ = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [new NestedSetBranch<TreeNode>(new TreeNode { NodeId = int.MaxValue })],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var heapBefore = GC.GetTotalMemory(forceFullCollection: true);

        // Act
        var plan = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [root],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var retained = GC.GetTotalMemory(forceFullCollection: true) - heapBefore;

        // Assert
        var budget = checked(BytesPerNodeBudget * NodeCount);
        _output.WriteLine(
            $"Nodes: {NodeCount}; retained bytes: {retained}; allocated bytes: {allocated}; budget bytes: {budget}");

        Assert.Equal(NodeCount, plan.Nodes.Count);
        Assert.InRange(retained, 1, budget);
        Assert.Empty(context.ChangeTracker.Entries());
        GC.KeepAlive(plan);
    }
}
