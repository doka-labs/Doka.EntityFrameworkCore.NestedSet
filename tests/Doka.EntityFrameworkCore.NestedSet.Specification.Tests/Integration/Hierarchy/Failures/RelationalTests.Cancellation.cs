namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class RelationalTests
{
    /// <summary>Rolls back cancellation after the database has applied a hierarchy update.</summary>
    /// <param name="operation">The mutation canceled after its first actual tree write.</param>
    [Theory]
    [InlineData("Move")]
    [InlineData("Delete")]
    [InlineData("DeleteSubtree")]
    public async Task CancellationAfterAppliedTreeWriteRestoresTheForest(
        string operation
    )
    {
        // Arrange
        var database = await _fixture.ResetAsync(Engine);
        await using var setup = database.CreateContext();
        var original = setup
            .NestedSet<TreeNode>()
            .ForScope(1);

        await SeedPromotionTreeAsync(setup, 1);
        var before = await SnapshotAsync(setup, 1);
        using var cancellation = new CancellationTokenSource();
        var probe = new AppliedTreeWriteCancellation(cancellation);
        await using var context = database.CreateContext(probe);
        var tree = context
            .NestedSet<TreeNode>()
            .ForScope(1);

        // Act
        var exception = await Record.ExceptionAsync(() => operation switch
        {
            "Move" => tree.MoveToAsync(3, 6, cancellation.Token),
            "Delete" => tree.DeleteAsync(3, cancellation.Token),
            "DeleteSubtree" => tree.DeleteSubtreeAsync(3, cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        });

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(probe.RowsChanged > 0);
        Assert.Equal(before, await SnapshotAsync(setup, 1));
        Assert.Null(context.Database.CurrentTransaction);
        Assert.True(
            (await original
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>Cancels after a completed update, excluding infrastructure-lock statements.</summary>
    private sealed class AppliedTreeWriteCancellation : DbCommandInterceptor
    {
        private readonly CancellationTokenSource _cancellation;

        /// <summary>Creates an observer tied to the operation token.</summary>
        internal AppliedTreeWriteCancellation(
            CancellationTokenSource cancellation
        )
        {
            _cancellation = cancellation;
        }

        /// <summary>Gets rows actually changed before cancellation was requested.</summary>
        internal int RowsChanged { get; private set; }

        /// <inheritdoc />
        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (RowsChanged == 0
                && result > 0
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("TreeNode", StringComparison.OrdinalIgnoreCase)
                && !NestedSetTestInfrastructure.ReferencesRegistry(eventData.Context, command.CommandText))
            {
                // WHY: Cancellation follows database execution; a before-command probe cannot prove write rollback.
                RowsChanged = result;
                await _cancellation.CancelAsync();
            }

            return result;
        }
    }
}
