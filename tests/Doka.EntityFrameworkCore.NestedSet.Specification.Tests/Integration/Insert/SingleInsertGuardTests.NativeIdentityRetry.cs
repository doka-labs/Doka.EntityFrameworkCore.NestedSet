namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>A converted mutable-key rejection leaves the same owned aggregate usable for retry.</summary>
    /// <param name="afterSave">Whether the prerequisite mutation occurs after persistence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignedCustomKeyFailureAllowsSingleInsertRetry(
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
        var input = NewCustomInput(Guid.NewGuid());
        var tree = context.NestedSet<SingleIdentityNode>();
        var stopMutation = AttachRetryKeyMutation(context, afterSave, () => input.Id.Value = 4);
        var rejection = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            input,
            Guid.Empty,
            CancellationToken.None));

        stopMutation();

        // Act
        await tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidStructure, Assert.IsType<NestedSetException>(rejection).Code);

        var saved = Assert.Single(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));

        Assert.Equal(3, saved.Id.Value);
        Assert.Equal("owned", saved.Details.Label);
        Assert.Equal(
            (3, Guid.Empty, 1L, 2L, 0, 0L),
            (input.Id.Value, input.TreeId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Null(input.ParentId);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.NotSame(
            input,
            await context.FindAsync<SingleIdentityNode>([new SingleIdentityKey(3)], CancellationToken.None));
    }

    /// <summary>A root or owned tracking rejection leaves the same aggregate usable for retry.</summary>
    /// <param name="owned">Whether the prerequisite failure occurs while tracking the owned payload.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSingleGraphTrackingAllowsRetry(
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
        var input = NewCustomInput(Guid.NewGuid());
        var tree = context.NestedSet<SingleIdentityNode>();
        var failure = AttachSingleTrackingFailure(context, input, owned);
        var rejection = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            input,
            Guid.Empty,
            CancellationToken.None));

        var triggered = failure.Triggered();

        // Act
        await tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, rejection);
        Assert.True(triggered);
        var saved = Assert.Single(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(3, saved.Id.Value);
        Assert.Equal("owned", saved.Details.Label);
        Assert.Equal(
            (3, Guid.Empty, 1L, 2L, 0, 0L),
            (input.Id.Value, input.TreeId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Null(input.ParentId);
        Assert.Same(
            marker,
            Assert.Single(context.ChangeTracker.Entries())
                .Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
    }

    /// <summary>A temporary or generated-key rejection leaves the sentinel input usable for retry.</summary>
    /// <param name="afterSave">Whether the prerequisite mutation occurs after persistence.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedSingleKeyFailureAllowsRetry(
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
        var input = NewGeneratedSingleInput(Guid.NewGuid());
        var tree = context
            .NestedSet<BulkStageBinaryScope>()
            .ForScope<byte[]>([1]);

        var stopMutation = AttachRetryKeyMutation(context, afterSave, () => input.Id = 99);
        var rejection = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            input,
            Guid.Empty,
            CancellationToken.None));

        stopMutation();

        // Act
        await tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidStructure,
            Assert.IsType<NestedSetException>(rejection)
                .Code);

        var saved = Assert.Single(
            await context
                .Set<BulkStageBinaryScope>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));

        Assert.True(input.Id > 0);
        Assert.Equal(input.Id, saved.Id);
        Assert.Equal<byte>([1], input.Scope);
        Assert.Null(input.ParentId);
        Assert.Equal((Guid.Empty, 1L, 2L, 0, 0L), (input.TreeId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.NotSame(input, await context.FindAsync<BulkStageBinaryScope>([input.Id], CancellationToken.None));
    }

    /// <summary>Fails the selected root or owned transition once, then leaves the callback inert for retry.</summary>
    private static (InvalidOperationException Error, Func<bool> Triggered) AttachSingleTrackingFailure(
        DbContext context,
        SingleIdentityNode input,
        bool owned
    )
    {
        var failure = new InvalidOperationException("Injected single insertion tracking failure.");
        var throwOnce = true;
        context.ChangeTracker.Tracking += (_, args) =>
        {
            if (throwOnce && ReferenceEquals(args.Entry.Entity, owned ? input.Details : input))
            {
                throwOnce = false;

                throw failure;
            }
        };

        return (failure, () => !throwOnce);
    }

    /// <summary>Creates sentinel-key input with nondefault structure shared by rejection and retry cases.</summary>
    private static BulkStageBinaryScope NewGeneratedSingleInput(
        Guid treeId
    ) => new()
    {
        Scope = [8],
        TreeId = treeId,
        ParentId = 9,
        Left = 71,
        Right = 72,
        Depth = 3,
        Position = 4,
    };
}
