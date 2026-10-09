namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class BulkStageGuardTests
{
    /// <summary>Rejects assigned integer-key changes before persistence and after the save callback.</summary>
    /// <param name="afterSave">Whether the mutation occurs in SavedChanges instead of SavingChanges.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackCannotReplaceAssignedIntegerKey(
        bool afterSave
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = context
            .NestedSet<BulkStageBinaryScope>()
            .ForScope<byte[]>([1]);

        var originalTreeId = Guid.NewGuid();
        var input = new BulkStageBinaryScope
        {
            Id = 3,
            Scope = [8],
            TreeId = originalTreeId,
            ParentId = 9,
            Left = 71,
            Right = 72,
            Depth = 5,
            Position = 7,
        };

        AttachAssignedKeyMutation(context, afterSave, () => input.Id = 4);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageBinaryScope, Guid>(
                    Guid.NewGuid(),
                    new NestedSetBranch<BulkStageBinaryScope>(input)),
            ],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal(
            (3, originalTreeId, 9, 71L, 72L, 5, 7L),
            (input.Id, input.TreeId, input.ParentId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Equal<byte>([8], input.Scope);
        Assert.Empty(
            await context
                .Set<BulkStageBinaryScope>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Rejects in-place assigned binary-key changes without losing the detached original identity.</summary>
    /// <param name="afterSave">Whether the mutation occurs in SavedChanges instead of SavingChanges.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackCannotMutateAssignedBinaryKeyInPlace(
        bool afterSave
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = context
            .NestedSet<BulkStageBinaryParent>()
            .ForScope(1);
        var originalTreeId = Guid.NewGuid();
        var input = new BulkStageBinaryParent
        {
            Id = [3],
            Scope = 8,
            TreeId = originalTreeId,
            ParentId = [9],
            Left = 71,
            Right = 72,
            Depth = 5,
            Position = 7,
        };

        // WHY: Replacing the array would miss aliasing defects. The callback mutates the exact tracked key buffer.
        AttachAssignedKeyMutation(context, afterSave, () => input.Id[0] = 4);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageBinaryParent, Guid>(
                    Guid.NewGuid(),
                    new NestedSetBranch<BulkStageBinaryParent>(input)),
            ],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal<byte>([3], input.Id);
        Assert.Equal<byte>([9], input.ParentId!);
        Assert.Equal(
            (8, originalTreeId, 71L, 72L, 5, 7L),
            (input.Scope, input.TreeId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Empty(
            await context
                .Set<BulkStageBinaryParent>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Places the same forbidden identity mutation on either side of the guarded payload save.</summary>
    private static void AttachAssignedKeyMutation(
        DbContext context,
        bool afterSave,
        Action mutate
    )
    {
        if (afterSave)
        {
            context.SavedChanges += (_, _) => mutate();
        }
        else
        {
            context.SavingChanges += (_, _) => mutate();
        }
    }
}
