namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Verifies exact single-insertion assignments independently of application value comparers.</summary>
public abstract class SingleInsertExactValueTests : ProviderTest
{
    private readonly BulkStageGuardFixture _fixture;

    /// <summary>Reuses the same native binary-collation and array mappings as bulk stage regressions.</summary>
    protected SingleInsertExactValueTests(
        IProviderFixture<BulkStageGuardFixture> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Preexisting comparer-equal aliases cannot prevent assignment of the actual destination.</summary>
    [Fact]
    public async Task ComparerEqualOriginalValuesDoNotBlockSingleInsertion()
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

        var input = new BulkStageTextNode
        {
            Id = "C",
            Scope = "s",
            ParentId = "a",
            Left = 71,
            Right = 72,
        };

        // Act
        await tree.InsertChildAsync(input, "A", CancellationToken.None);

        // Assert
        Assert.Equal(("S", "A", 2, 3, 1), (input.Scope, input.ParentId, input.Left, input.Right, input.Depth));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(
            (await tree
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).Issues);
        Assert.Equal(
            2,
            await tree
                .InTree(Guid.Empty)
                .Nodes
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Rejected callback aliases restore the exact pre-call spelling despite a broader comparer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedAliasRestoresExactSingleInsertionInput(
        bool parent
    )
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
        var scope = parent ? "S" : "A";
        var tree = context
            .NestedSet<BulkStageTextNode>()
            .ForScope(scope);

        var input = new BulkStageTextNode
        {
            Id = "C",
            Scope = scope,
            ParentId = parent ? "A" : null,
            Left = 71,
            Right = 72,
        };

        context.SavingChanges += (_, _) =>
        {
            if (parent)
            {
                input.ParentId = "a";
            }
            else
            {
                input.Scope = "a";
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() => parent
            ? tree.InsertChildAsync(input, "A", CancellationToken.None)
            : tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal(scope, input.Scope);
        Assert.Equal(parent ? "A" : null, input.ParentId);
        Assert.Equal((71, 72), (input.Left, input.Right));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, await context.Set<BulkStageTextNode>().CountAsync(CancellationToken.None));
    }

    /// <summary>Even an initially shared scope array is separated from the service before callbacks execute.</summary>
    [Fact]
    public async Task SharedInitialScopeArrayCannotMutateSingleInsertionService()
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
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal<byte>([1], scope);
        Assert.Equal<byte>([1], input.Scope);
        Assert.Equal((71, 72), (input.Left, input.Right));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await context.Set<BulkStageBinaryScope>().ToArrayAsync(CancellationToken.None));
    }

    /// <summary>A failed save cannot overwrite the pre-call key snapshot by mutating its byte array.</summary>
    [Fact]
    public async Task FailedSingleInsertionRestoresMutableOriginalKey()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(Engine);
        var tree = context
            .NestedSet<BulkStageBinaryParent>()
            .ForScope(1);

        var input = new BulkStageBinaryParent
        {
            Id = [3],
            Scope = 17,
            Left = 71,
            Right = 72,
        };

        var failure = new InvalidOperationException("Injected failure after mutating a saved binary key.");
        context.SavedChanges += (_, _) =>
        {
            input.Id[0] = 4;

            throw failure;
        };

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Same(failure, error);
        Assert.Equal<byte>([3], input.Id);
        Assert.Equal((17, 71, 72), (input.Scope, input.Left, input.Right));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await context.Set<BulkStageBinaryParent>().ToArrayAsync(CancellationToken.None));
    }
}
