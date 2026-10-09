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

    /// <summary>A wide plan reserves heap even when every input needs complete structural rollback state.</summary>
    /// <param name="prefilled">Whether inputs carry caller-owned non-sentinel structure before planning.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WidePlanStaysWithinMemoryBudget(
        bool prefilled
    )
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
            .Select(id => new NestedSetBranch<TreeNode>(new TreeNode
            {
                NodeId = id,
                Tree = prefilled ? 7 : 0,
                TreeId = prefilled ? new Guid("11111111-1111-1111-1111-111111111111") : Guid.Empty,
                Parent = prefilled ? 999 : null,
                Start = prefilled ? 17L : 0L,
                End = prefilled ? 18L : 0L,
                Depth = prefilled ? 9 : 0,
                Position = prefilled ? 11L : 0L,
            }))
            .ToArray();

        var root = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);
        _ = context.Model;

        // WHY: Prefilled rollback compiles additional metadata snapshot delegates lazily. Warm the same value
        // shape before measuring per-node retained state so one-time model caches are not multiplied by node count.
        _ = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [new NestedSetBranch<TreeNode>(new TreeNode
            {
                NodeId = int.MaxValue,
                Tree = prefilled ? 7 : 0,
                TreeId = prefilled ? new Guid("11111111-1111-1111-1111-111111111111") : Guid.Empty,
                Parent = prefilled ? 999 : null,
                Start = prefilled ? 17L : 0L,
                End = prefilled ? 18L : 0L,
                Depth = prefilled ? 9 : 0,
                Position = prefilled ? 11L : 0L,
            })],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var heapBefore = ManagedHeapObservation.CollectedBytes();

        // Act
        var plan = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [root],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var retained = ManagedHeapObservation.CollectedBytes() - heapBefore;

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
