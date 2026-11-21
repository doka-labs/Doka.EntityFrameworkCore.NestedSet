using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks public forest preparation overhead before any database connection or payload write.</summary>
public sealed class ForestPreparationBoundaryTests
{
    private const int NodeCount = 20_000;
    /// <summary>A single tree adds bounded orchestration overhead without another pair of per-node indexes.</summary>
    /// <returns>A task that completes after preparation stops at its first connection boundary.</returns>
    [Fact]
    public async Task InsertForestAsync_SingleTreePreparation_RestoresInputsBeforeDatabaseIo()
    {
        // Arrange
        var boundary = new PreparationBoundary();
        var options = new DbContextOptionsBuilder<TreeContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .AddInterceptors(boundary)
            .Options;

        await using var context = new TreeContext(options);
        var hierarchy = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        var children = Enumerable
            .Range(2, NodeCount - 1)
            .Select(key => new NestedSetBranch<TreeNode>(new TreeNode { NodeId = key }))
            .ToArray();

        var root = new NestedSetBranch<TreeNode>(new TreeNode { NodeId = 1 }, children);
        NestedSetTreeImport<TreeNode, Guid>[] imports = [new(Guid.Empty, root)];
        boundary.Start();

        // Act
        var error = await Record.ExceptionAsync(() => hierarchy.InsertForestAsync(imports, CancellationToken.None));

        // Assert
        Assert.IsType<PreparationStoppedException>(error);
        Assert.True(boundary.Reached);
        Assert.Equal(boundary.StartThread, boundary.EndThread);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        Assert.All(children, child => Assert.Equal((0L, 0L), (child.Entity.Start, child.Entity.End)));
    }

    /// <summary>Stops the public import at its first physical connection attempt.</summary>
    private sealed class PreparationBoundary : DbConnectionInterceptor
    {
        /// <summary>Gets whether the public import reached the connection boundary.</summary>
        internal bool Reached { get; private set; }

        /// <summary>Gets the thread owning the start of preparation.</summary>
        internal int StartThread { get; private set; }

        /// <summary>Gets the thread owning its connection boundary.</summary>
        internal int EndThread { get; private set; }

        /// <summary>Begins a fresh observation after all caller-owned input and model setup exist.</summary>
        internal void Start()
        {
            Reached = false;
            StartThread = Environment.CurrentManagedThreadId;
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
            Reached = true;

            // WHY: The deliberate failure occurs before physical I/O; the public operation must restore every input.
            return ValueTask.FromException<InterceptionResult>(new PreparationStoppedException());
        }
    }

    /// <summary>Distinguishes the intentional pre-write stop from an actual import validation failure.</summary>
    private sealed class PreparationStoppedException : Exception;
}
