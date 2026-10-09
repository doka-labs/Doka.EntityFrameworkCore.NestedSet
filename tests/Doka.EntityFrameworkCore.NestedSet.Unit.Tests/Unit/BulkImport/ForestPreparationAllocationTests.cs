using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks public forest preparation overhead before any database connection or payload write.</summary>
[Collection("BulkPlanAllocation")]
public sealed class ForestPreparationAllocationTests
{
    private const int NodeCount = 20_000;
    private readonly ITestOutputHelper _output;

    /// <summary>Retains measured allocation evidence in the test result.</summary>
    /// <param name="output">The sink receiving deterministic preparation measurements.</param>
    public ForestPreparationAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>A single tree adds bounded orchestration overhead without another pair of per-node indexes.</summary>
    /// <returns>A task that completes after preparation stops at its first connection boundary.</returns>
    [Fact]
    public async Task SingleTreePreparationDoesNotRepeatPlanIdentityIndexes()
    {
        // Arrange
        var boundary = new PreparationBoundary();
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:")
            .UseNestedSets()
            .AddInterceptors(boundary)
            .Options;

        await using var context = new TreeContext(options);
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var warm = new NestedSetTreeImport<TreeNode, Guid>(
            Guid.Empty,
            new NestedSetBranch<TreeNode>(new TreeNode { NodeId = int.MaxValue }));

        // WHY: Warm model, framework dispatch and the connection boundary before comparing per-node work.
        _ = await Record.ExceptionAsync(() => hierarchy.InsertForestAsync([warm], CancellationToken.None));
        var children = Enumerable
            .Range(2, NodeCount - 1)
            .Select(key => new NestedSetBranch<TreeNode>(new TreeNode { NodeId = key }))
            .ToArray();

        var root = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);
        var imports = new[] { new NestedSetTreeImport<TreeNode, Guid>(Guid.Empty, root) };
        var store = new NestedSetStore<TreeNode, int, Guid, int>(
            context,
            context.Model.FindEntityType(typeof(TreeNode))!,
            1,
            Guid.Empty);

        var beforePlan = GC.GetAllocatedBytesForCurrentThread();
        var plan = new NestedSetBulkPlan<TreeNode, int, Guid, int>(
            store,
            [root],
            new HashSet<TreeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var planAllocated = GC.GetAllocatedBytesForCurrentThread() - beforePlan;

        // WHY: Allow one reference per input plus fixed orchestration costs; duplicate identity indexes exceed it.
        var maximumOverhead = (NodeCount * (long)IntPtr.Size) + (64 * 1024);
        boundary.Start();

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertForestAsync(imports, CancellationToken.None));

        // Assert
        _output.WriteLine(
            $"Nodes={NodeCount}; plan allocated={planAllocated}; forest preparation allocated={boundary.Allocated}; "
            + $"additional allocation budget={maximumOverhead}.");

        Assert.IsType<PreparationStoppedException>(error);
        Assert.True(boundary.Reached);
        Assert.Equal(boundary.StartThread, boundary.EndThread);
        Assert.InRange(boundary.Allocated - planAllocated, 0, maximumOverhead);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        Assert.All(children, child => Assert.Equal((0L, 0L), (child.Entity.Start, child.Entity.End)));
        GC.KeepAlive(plan);
    }

    /// <summary>Stops at the first connection attempt and captures only synchronous pre-write allocation.</summary>
    private sealed class PreparationBoundary : DbConnectionInterceptor
    {
        private long _before;

        /// <summary>Gets whether the public import reached the connection boundary.</summary>
        internal bool Reached { get; private set; }

        /// <summary>Gets the allocation at the preparation boundary, excluding failure restoration.</summary>
        internal long Allocated { get; private set; }

        /// <summary>Gets the thread owning the start of preparation.</summary>
        internal int StartThread { get; private set; }

        /// <summary>Gets the thread owning its connection boundary.</summary>
        internal int EndThread { get; private set; }

        /// <summary>Begins a fresh observation after all caller-owned input and model setup exist.</summary>
        internal void Start()
        {
            Reached = false;
            StartThread = Environment.CurrentManagedThreadId;
            _before = GC.GetAllocatedBytesForCurrentThread();
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            EndThread = Environment.CurrentManagedThreadId;
            Allocated = GC.GetAllocatedBytesForCurrentThread() - _before;
            Reached = true;

            // WHY: The measured work ends before I/O; restoring inputs after this deliberate failure is unmeasured.
            return ValueTask.FromException<InterceptionResult>(new PreparationStoppedException());
        }
    }

    /// <summary>Distinguishes the intentional pre-write stop from an actual import validation failure.</summary>
    private sealed class PreparationStoppedException : Exception;
}
