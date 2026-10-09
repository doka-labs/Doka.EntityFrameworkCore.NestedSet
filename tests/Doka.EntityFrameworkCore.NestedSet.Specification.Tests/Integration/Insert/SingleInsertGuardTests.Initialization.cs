namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SingleInsertGuardTests
{
    /// <summary>Early getter, snapshot, and foreign-navigation failures release the initial detached input.</summary>
    /// <param name="fault">Selects getter, snapshot, or populated non-owned navigation rejection.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RejectedSingleInitializationReleasesInputAndPreservesMarker(
        int fault
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

        // Act
        var rejected = await FailSingleInitializationAsync(context, fault);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        var retained = (rejected.Root.IsAlive, rejected.Owned.IsAlive, rejected.Foreign?.IsAlive ?? false);

        // Assert
        Assert.True(rejected.Restored);
        Assert.Equal((false, false, false), retained);
        Assert.Same(marker, Assert.Single(context.ChangeTracker.Entries()).Entity);
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
        GC.KeepAlive(context);
    }

    /// <summary>Leaves no caller-owned strong reference when observing a rejected detached initialization.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Root, WeakReference Owned, WeakReference? Foreign, bool Restored)>
        FailSingleInitializationAsync(
            SingleIdentityContext context,
            int fault
        )
    {
        var treeId = Guid.NewGuid();
        var input = NewCustomInput(treeId);
        var failure = InjectSingleInitializationFault(input, fault);

        var rootReference = new WeakReference(input);
        var ownedReference = new WeakReference(input.Details);
        var foreign = input.Foreign ?? input.Details.Foreign;
        var foreignReference = foreign is null ? null : new WeakReference(foreign);
        var error = await Record.ExceptionAsync(() => context
            .NestedSet<SingleIdentityNode>()
            .InsertRootAsync(input, Guid.Empty, CancellationToken.None));

        ClearSingleInitializationFault(input);
        var restored =
            (fault >= 2
                ? error is NestedSetException { Code: NestedSetErrorCode.InvalidContext }
                : ReferenceEquals(error, failure))
            && (input.Id.Value, input.TreeId, input.ParentId?.Value, input.Left, input.Right, input.Depth,
                input.Position)
            == (3, treeId, 8, 71, 72, 3, 4);

        return (rootReference, ownedReference, foreignReference, restored);
    }
}
