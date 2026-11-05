using System.Data.Common;
using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class HierarchyChangeDetectionTests
{
    /// <summary>Registered ordinary saves preserve false without discovering unmarked hierarchy payload.</summary>
    /// <param name="synchronous">Whether to exercise the explicitly synchronous override contract.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualBoundaryPreservesUnmarkedPayloadAndNeverDetects(
        bool synchronous
    )
    {
        // Arrange
        var commands = new ManualCommandCounter();
        var options = new DbContextOptionsBuilder<SnapshotContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .AddInterceptors(commands)
            .Options;

        await using var context = new SnapshotContext(options);
        var node = CreateNode();
        var entry = context.Attach(node);
        var detections = 0;
        bool? savingFlag = null;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        context.SavingChanges += (_, _) => savingFlag = context.ChangeTracker.AutoDetectChangesEnabled;
        node.Payload = "Pending unmarked payload";

        // Act
        var saved = await SaveManualBoundaryAsync(context, synchronous,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, saved);
        Assert.Equal(false, savingFlag);
        Assert.Equal(0, detections);
        Assert.Equal(0, commands.Count);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(entry.Property(value => value.Payload).IsModified);
        Assert.Equal(string.Empty,
            entry.Property(value => value.Payload).OriginalValue);
        Assert.Equal("Pending unmarked payload", node.Payload);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Unmarked Parent/order edits are rejected without detection, flag mutation, or SQL.</summary>
    /// <param name="synchronous">Whether to exercise the explicitly synchronous override contract.</param>
    /// <param name="propertyName">The unmarked hierarchy trigger edited directly on the CLR entity.</param>
    [Theory]
    [InlineData(false, nameof(ProbeNode.ParentId))]
    [InlineData(false, nameof(ProbeNode.Name))]
    [InlineData(true, nameof(ProbeNode.ParentId))]
    [InlineData(true, nameof(ProbeNode.Name))]
    public async Task ManualBoundaryRejectsUnmarkedTriggerBeforeSql(
        bool synchronous,
        string propertyName
    )
    {
        // Arrange
        var commands = new ManualCommandCounter();
        var options = new DbContextOptionsBuilder<SnapshotContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.UseNestedSets())
            .AddInterceptors(commands)
            .Options;

        await using var context = new SnapshotContext(options);
        var node = CreateNode();
        var entry = context.Attach(node);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        ChangeRole(node, propertyName);

        // Act
        var failure = await Record.ExceptionAsync(() => SaveManualBoundaryAsync(context, synchronous,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, commands.Count);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.False(entry.Property(propertyName).IsModified);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    /// <summary>Calls the real base EF save through the same internal coordinator used by overrides.</summary>
    /// <param name="context">The context used by this fixture operation.</param>
    /// <param name="synchronous">Whether the fixture deliberately exercises the synchronous save contract.</param>
    /// <param name="cancellationToken">The caller's cancellation token for this fixture operation.</param>
    private static Task<int> SaveManualBoundaryAsync(
        SnapshotContext context,
        bool synchronous,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (synchronous)
        {
            // WHY: This branch intentionally exercises an explicitly synchronous application override.
            // ReSharper disable once MethodHasAsyncOverload
            return Task.FromResult(NestedSetSaveChanges.ExecuteSynchronous(context, () => context.SaveChanges(false)));
        }

        return NestedSetSaveChanges.ExecuteAsync(context,
            false,
            token => context.SaveChangesAsync(false, token),
            cancellationToken);
    }

    /// <summary>Observes any provider command without replacing EF's actual persistence path.</summary>
    private sealed class ManualCommandCounter : DbCommandInterceptor
    {
        /// <summary>Gets the commands issued while a strict manual-detection boundary is active.</summary>
        internal int Count { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Count++;

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Count++;

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            Count++;

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Count++;

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result
        )
        {
            Count++;

            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default
        )
        {
            Count++;

            return ValueTask.FromResult(result);
        }
    }
}
