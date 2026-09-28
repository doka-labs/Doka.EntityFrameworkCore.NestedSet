namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class BulkStageGuardTests
{
    /// <summary>Staging overwrites equivalent CLR aliases with the exact requested native scope and parent.</summary>
    [Fact]
    public async Task ComparerEqualOriginalValuesDoNotPreventExactStaging()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await context.AddAsync(
            new BulkStageTextNode
            {
                Id = "A",
                Scope = "A",
                Left = 1,
                Right = 2,
            },
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("A");

        var input = new BulkStageTextNode
        {
            Id = "C",
            Scope = "a",
            ParentId = "a",
        };

        // Act
        await tree.InsertSubtreeAsync(new NestedSetBranch<BulkStageTextNode>(input), "A", CancellationToken.None);

        // Assert
        Assert.Equal(("A", "A", 2, 3), (input.Scope, input.ParentId, input.Left, input.Right));
        Assert.Equal(
            2,
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .CountAsync(CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Rollback restores the original representation even when the callback value compares equal.</summary>
    [Fact]
    public async Task RejectedCallbackRestoresComparerEqualOriginalRepresentations()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await context.AddAsync(
            new BulkStageTextNode
            {
                Id = "B",
                Scope = "B",
                Left = 1,
                Right = 2,
            },
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("B");

        var input = new BulkStageTextNode
        {
            Id = "C",
            Scope = "A",
            ParentId = "A",
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) =>
        {
            input.Scope = "a";
            input.ParentId = "a";
        };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(
            new NestedSetBranch<BulkStageTextNode>(input),
            "B",
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal(("A", "A", 71, 72), (input.Scope, input.ParentId, input.Left, input.Right));
        Assert.Single(
            await context
                .Set<BulkStageTextNode>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
    }

    /// <summary>Staging isolates an initially shared scope array before a callback mutates the imported node.</summary>
    [Fact]
    public async Task InitiallySharedBinaryScopeCannotMutateServiceArgument()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        byte[] scope = [1];
        var tree = context
            .NestedSet<BulkStageBinaryScope>()
            .ForScope(scope);

        var input = new BulkStageBinaryScope
        {
            Id = 1,
            Scope = scope,
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) => input.Scope[0] = 2;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageBinaryScope, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkStageBinaryScope>(input)),
            ],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal<byte>([1], scope);
        Assert.Equal<byte>([1], input.Scope);
        Assert.Equal((71, 72), (input.Left, input.Right));
        Assert.Empty(
            await context
                .Set<BulkStageBinaryScope>()
                .ToArrayAsync(CancellationToken.None));
    }
}
