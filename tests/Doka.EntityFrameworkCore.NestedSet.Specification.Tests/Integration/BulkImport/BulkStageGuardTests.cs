namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Rejects callback changes using exact staged values independently of application key comparers.</summary>
public abstract partial class BulkStageGuardTests : ProviderTest
{
    private readonly BulkStageGuardFixture _fixture;

    /// <summary>Shares native case-sensitive and mutable binary mappings on all supported engines.</summary>
    protected BulkStageGuardTests(
        IProviderFixture<BulkStageGuardFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>A broader parent comparer cannot redirect an imported child to a distinct native root.</summary>
    [Fact]
    public async Task CallbackCannotChangeParentToComparerEqualDatabaseDistinctKey()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await context.AddRangeAsync(
            [
                new BulkStageTextNode
                {
                    Id = "A",
                    Scope = "S",
                    Left = 1,
                    Right = 4,
                },
                new BulkStageTextNode
                {
                    Id = "a",
                    Scope = "S",
                    ParentId = "A",
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                },
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("S");

        var input = new BulkStageTextNode
        {
            Id = "C",
            Scope = "original",
            ParentId = "original",
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) => input.ParentId = "a";

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync(
            new NestedSetBranch<BulkStageTextNode>(input),
            "A",
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal(("original", "original", 71, 72), (input.Scope, input.ParentId, input.Left, input.Right));
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
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A broader scope comparer cannot authorize moving a node into another native forest.</summary>
    [Fact]
    public async Task CallbackCannotChangeScopeToComparerEqualDatabaseDistinctValue()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("A");

        var input = new BulkStageTextNode
        {
            Id = "C",
            Scope = "original",
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) => input.Scope = "a";

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageTextNode, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkStageTextNode>(input)),
            ],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal(("original", 71, 72), (input.Scope, input.Left, input.Right));
        Assert.Empty(
            await context
                .Set<BulkStageTextNode>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A staged parent snapshot cannot share the byte array that a save callback mutates in place.</summary>
    [Fact]
    public async Task CallbackCannotMutateStagedBinaryParentInPlace()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await context.AddRangeAsync(
            [
                new BulkStageBinaryParent
                {
                    Id = [1],
                    Scope = 1,
                    Left = 1,
                    Right = 4,
                },
                new BulkStageBinaryParent
                {
                    Id = [2],
                    Scope = 1,
                    ParentId = [1],
                    Left = 2,
                    Right = 3,
                    Depth = 1,
                },
            ],
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var tree = context
            .NestedSet<BulkStageBinaryParent>()
            .ForScope(1);

        var input = new BulkStageBinaryParent
        {
            Id = [3],
            Scope = 17,
            ParentId = [8],
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) => input.ParentId![0] = 2;

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertSubtreeAsync<byte[]>(
            new NestedSetBranch<BulkStageBinaryParent>(input),
            [1],
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal<byte>([8], input.ParentId!);
        Assert.Equal((17, 71, 72), (input.Scope, input.Left, input.Right));
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

    /// <summary>Callbacks cannot mutate the facade scope through a staged node's shared byte array.</summary>
    [Fact]
    public async Task CallbackCannotMutateStagedBinaryScopeInPlace()
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
            Scope = [8],
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
        Assert.Equal<byte>([8], input.Scope);
        Assert.Equal((71, 72), (input.Left, input.Right));
        Assert.Empty(
            await context
                .Set<BulkStageBinaryScope>()
                .ToArrayAsync(CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Custom application comparers remain supported when callbacks preserve the staged values.</summary>
    [Fact]
    public async Task UnchangedStagedValuesRetainCustomComparerSupport()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        await context.AddAsync(
            new BulkStageTextNode
            {
                Id = "A",
                Scope = "S",
                Left = 1,
                Right = 2,
            },
            CancellationToken.None);

        await context.SavePrecomputedHierarchyAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope("S");

        var input = new BulkStageTextNode { Id = "C" };

        // Act
        await tree.InsertSubtreeAsync(new NestedSetBranch<BulkStageTextNode>(input), "A", CancellationToken.None);

        // Assert
        Assert.Equal(("S", "A", 2, 3, 1), (input.Scope, input.ParentId, input.Left, input.Right, input.Depth));
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A separate array with identical structural bytes is not an unauthorized scope change.</summary>
    [Fact]
    public async Task EquivalentBinaryScopeReplacementRemainsSupported()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        byte[] scope = [1];
        var tree = context
            .NestedSet<BulkStageBinaryScope>()
            .ForScope(scope);

        var input = new BulkStageBinaryScope { Id = 1 };
        context.SavingChanges += (_, _) => input.Scope = input.Scope.ToArray();

        // Act
        await tree.InsertForestAsync(
            [
                new NestedSetTreeImport<BulkStageBinaryScope, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<BulkStageBinaryScope>(input)),
            ],
            CancellationToken.None);

        // Assert
        Assert.Equal(scope, input.Scope);
        Assert.NotSame(scope, input.Scope);
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Single(
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .ToArrayAsync(CancellationToken.None));
    }
}
