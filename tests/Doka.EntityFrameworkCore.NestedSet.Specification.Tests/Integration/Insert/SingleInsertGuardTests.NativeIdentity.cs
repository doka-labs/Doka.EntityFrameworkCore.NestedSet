namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>Rejects mutable converted-key callbacks while preserving exact rollback and retry identities.</summary>
    /// <param name="afterSave">Whether the mutation occurs after persistence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignedCustomKeyMutationRollsBackSingleInsert(
        bool afterSave
    )
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var originalTreeId = Guid.NewGuid();
        var input = NewCustomInput(originalTreeId);
        var tree = context.NestedSet<SingleIdentityNode>();

        // WHY: A converted reference key can change its provider value and native hash in place.
        AttachSingleKeyMutation(context, afterSave, () => input.Id.Value = 4);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));
        var restored = (input.Id.Value, input.TreeId, input.ParentId?.Value, input.Left, input.Right, input.Depth, input.Position);
        var tracked = context
            .ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .ToArray();

        var originalLookup = await context.FindAsync<SingleIdentityNode>(
            [new SingleIdentityKey(3)],
            CancellationToken.None);

        var changedLookup = await context.FindAsync<SingleIdentityNode>(
            [new SingleIdentityKey(4)],
            CancellationToken.None);

        var rolledBackRows = await context
            .Set<SingleIdentityNode>()
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidStructure,
            Assert.IsType<NestedSetException>(error)
                .Code);
        Assert.Equal((3, originalTreeId, (int?)8, 71L, 72L, 3, 4L), restored);
        Assert.Same(marker, Assert.Single(tracked));
        Assert.Null(originalLookup);
        Assert.Null(changedLookup);
        Assert.Equal(0, rolledBackRows);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A rejected root or owned tracking callback leaves no hidden identity and preserves the input.</summary>
    /// <param name="owned">Whether the failure occurs while tracking the owned payload.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSingleGraphTrackingRestoresInput(
        bool owned
    )
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var originalTreeId = Guid.NewGuid();
        var input = NewCustomInput(originalTreeId);
        var failure = AttachSingleTrackingFailure(context, input, owned);

        var tree = context.NestedSet<SingleIdentityNode>();

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));
        var restored = (input.Id.Value, input.TreeId, input.ParentId?.Value, input.Left, input.Right, input.Depth, input.Position);
        var tracked = context
            .ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .ToArray();

        var lookup = await context.FindAsync<SingleIdentityNode>([new SingleIdentityKey(3)], CancellationToken.None);
        var rolledBackRows = await context
            .Set<SingleIdentityNode>()
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, error);
        Assert.True(failure.Triggered());
        Assert.Equal((3, originalTreeId, (int?)8, 71L, 72L, 3, 4L), restored);
        Assert.Same(marker, Assert.Single(tracked));
        Assert.Null(lookup);
        Assert.Equal(0, rolledBackRows);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Successful generated single insertions detach the installed database identities.</summary>
    [Fact]
    public async Task GeneratedSingleIdentitiesPersistAndCanBeFoundAfterDetach()
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var tree = context
            .NestedSet<BulkStageBinaryScope>()
            .ForScope<byte[]>([1]);

        var root = new BulkStageBinaryScope();
        var child = new BulkStageBinaryScope();

        await tree.InsertRootAsync(root, Guid.Empty, CancellationToken.None);

        // Act
        await tree.InsertChildAsync(child, root.Id, CancellationToken.None);
        var tracked = context
            .ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .ToArray();

        var found = await context.FindAsync<BulkStageBinaryScope>([root.Id], CancellationToken.None);

        // Assert
        Assert.True(root.Id > 0);
        Assert.True(child.Id > root.Id);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Same(marker, Assert.Single(tracked));
        Assert.NotSame(root, found);
        Assert.Equal(root.Id, Assert.IsType<BulkStageBinaryScope>(found).Id);
        Assert.Equal(
            2,
            await context
                .Set<BulkStageBinaryScope>()
                .CountAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Callbacks cannot replace temporary or generated identities during a single insertion.</summary>
    /// <param name="afterSave">Whether the mutation replaces the persisted generated identity.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedSingleKeyMutationRollsBackInput(
        bool afterSave
    )
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var originalTreeId = Guid.NewGuid();
        var input = NewGeneratedSingleInput(originalTreeId);

        var tree = context
            .NestedSet<BulkStageBinaryScope>()
            .ForScope<byte[]>([1]);

        var generatedKey = 0;
        context.SavedChanges += (_, _) => generatedKey = input.Id;
        AttachSingleKeyMutation(context, afterSave, () => input.Id = 99);

        // Act
        var error = await Record.ExceptionAsync(() => tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None));
        var restored = (input.Id, input.TreeId, input.ParentId, input.Left, input.Right, input.Depth, input.Position);
        var restoredScope = input.Scope.ToArray();
        var tracked = context
            .ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .ToArray();

        var changedLookup = await context.FindAsync<BulkStageBinaryScope>([99], CancellationToken.None);
        var generatedLookup = await context.FindAsync<BulkStageBinaryScope>([generatedKey], CancellationToken.None);
        var rolledBackRows = await context
            .Set<BulkStageBinaryScope>()
            .AsNoTracking()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(error).Code);
        Assert.Equal((0, originalTreeId, (int?)9, 71L, 72L, 3, 4L), restored);
        Assert.Equal<byte>([8], restoredScope);
        Assert.Same(marker, Assert.Single(tracked));
        Assert.Null(changedLookup);
        Assert.Null(generatedLookup);
        Assert.Equal(0, rolledBackRows);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>Creates nondefault detached structure whose exact values must survive rollback.</summary>
    private static SingleIdentityNode NewCustomInput(
        Guid treeId
    ) => new()
    {
        Id = new SingleIdentityKey(3),
        TreeId = treeId,
        ParentId = new SingleIdentityKey(8),
        Left = 71,
        Right = 72,
        Depth = 3,
        Position = 4,
        Details = new SingleIdentityDetails { Label = "owned" },
    };
}
