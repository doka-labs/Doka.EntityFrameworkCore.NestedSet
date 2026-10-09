namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>A final lifecycle failure restores SQL, owned identity, generated payload, and structure.</summary>
    /// <param name="afterChange">Whether the callback fails after the native state transition.</param>
    /// <param name="owned">Whether the callback rejects the owned payload's detachment.</param>
    /// <param name="callerTransaction">Whether rollback must retain the caller's surrounding transaction.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task FinalSingleDetachmentFailureRollsBackGeneratedPayload(
        bool afterChange,
        bool owned,
        bool callerTransaction
    )
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var treeId = Guid.NewGuid();
        var input = NewCustomInput(treeId);
        var details = input.Details;
        var ownershipGeneration =
            context.Model.FindEntityType(typeof(SingleIdentityNode))!.FindNavigation(nameof(SingleIdentityNode.Details))
                !.TargetEntityType.FindProperty(nameof(SingleIdentityDetails.OwnerId))!.ValueGenerated;

        var failure = AttachSingleDetachmentFailure(context, input, afterChange, owned);
        var generatedObserved = false;
        context.SavingChanges += (_, _) => details.Label = "callback payload";
        context.SavedChanges += (_, _) => generatedObserved = input.GeneratedValue == 42 && details is { GeneratedValue: 43, OwnerId.Value: 3 };

        // Act
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<SingleIdentityNode>()
            .InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        // Assert
        Assert.Contains(
            failure.Error,
            Assert
                .IsType<AggregateException>(error)
                .Flatten()
                .InnerExceptions);
        Assert.True(failure.Triggered());
        Assert.True(generatedObserved);
        Assert.Equal(ValueGenerated.Never, ownershipGeneration);
        Assert.Equal(0, details.OwnerId.Value);
        Assert.Equal((0, 0), (input.GeneratedValue, details.GeneratedValue));
        Assert.Equal("callback payload", details.Label);
        Assert.Equal(
            (3, treeId, 8, 71L, 72L, 3, 4L),
            (input.Id.Value, input.TreeId, input.ParentId?.Value, input.Left, input.Right, input.Depth,
                input.Position));
        Assert.Same(details, input.Details);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Null(await context.FindAsync<SingleIdentityNode>([new SingleIdentityKey(3)], CancellationToken.None));
        Assert.Empty(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
    }

    /// <summary>A one-shot final lifecycle rejection leaves the corrected same aggregate usable for retry.</summary>
    /// <param name="afterChange">Whether the prerequisite failure follows the native state transition.</param>
    /// <param name="owned">Whether the prerequisite failure rejects the owned payload's detachment.</param>
    /// <param name="callerTransaction">Whether both attempts use the caller's surrounding transaction.</param>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task FinalSingleDetachmentFailureAllowsInputRetry(
        bool afterChange,
        bool owned,
        bool callerTransaction
    )
    {
        // Arrange
        await using var database = new ModelCompatibilityDatabase();
        await using var context = await database.CreateContextAsync<SingleIdentityContext>(
            Engine,
            static options => new SingleIdentityContext(options));

        await using var transaction = callerTransaction
            ? await context.Database.BeginTransactionAsync(
                Engine == "Sqlite" ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;

        var marker = new UnrelatedRow { Id = 701 };
        await context.AddAsync(marker, CancellationToken.None);
        await context.SaveChangesAsync(CancellationToken.None);
        var input = NewCustomInput(Guid.NewGuid());
        var details = input.Details;
        var tree = context.NestedSet<SingleIdentityNode>();
        var failure = AttachSingleDetachmentFailure(context, input, afterChange, owned);
        var rejection = await Record.ExceptionAsync(() => tree.InsertRootAsync(
            input,
            Guid.Empty,
            CancellationToken.None));

        var triggered = failure.Triggered();

        // Act
        await tree.InsertRootAsync(input, Guid.Empty, CancellationToken.None);

        // Assert
        Assert.Contains(
            failure.Error,
            Assert
                .IsType<AggregateException>(rejection)
                .Flatten()
                .InnerExceptions);
        Assert.True(triggered);
        Assert.Same(details, input.Details);
        Assert.Equal((42, 43, 3), (input.GeneratedValue, details.GeneratedValue, details.OwnerId.Value));
        Assert.Equal(
            (3, Guid.Empty, 1L, 2L, 0, 0L),
            (input.Id.Value, input.TreeId, input.Left, input.Right, input.Depth, input.Position));
        Assert.Null(input.ParentId);
        var saved = Assert.Single(
            await context
                .Set<SingleIdentityNode>()
                .AsNoTracking()
                .ToArrayAsync(CancellationToken.None));
        Assert.Equal(
            (3, 42, 43, 3),
            (saved.Id.Value, saved.GeneratedValue, saved.Details.GeneratedValue, saved.Details.OwnerId.Value));
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(
            1,
            await context
                .Set<UnrelatedRow>()
                .CountAsync(CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.NotSame(
            input,
            await context.FindAsync<SingleIdentityNode>([new SingleIdentityKey(3)], CancellationToken.None));
    }

    /// <summary>Rejects one final public detachment while keeping failure cleanup and retry usable.</summary>
    private static (InvalidOperationException Error, Func<bool> Triggered) AttachSingleDetachmentFailure(
        DbContext context,
        SingleIdentityNode input,
        bool afterChange,
        bool owned
    )
    {
        var failure = new InvalidOperationException("Injected final single detachment failure.");
        var throwOnce = true;
        Action<EntityEntry, EntityState> reject = (entry, state) =>
        {
            if (throwOnce
                && state == EntityState.Detached
                && ReferenceEquals(entry.Entity, owned ? input.Details : input))
            {
                throwOnce = false;

                throw failure;
            }
        };

        if (afterChange)
        {
            context.ChangeTracker.StateChanged += (_, args) => reject(args.Entry, args.NewState);
        }
        else
        {
            context.ChangeTracker.StateChanging += (_, args) => reject(args.Entry, args.NewState);
        }

        return (failure, () => !throwOnce);
    }
}
