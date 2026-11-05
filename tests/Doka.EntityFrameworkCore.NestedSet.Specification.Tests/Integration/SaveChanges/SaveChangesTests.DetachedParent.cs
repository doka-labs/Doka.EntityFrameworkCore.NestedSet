namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>A detached old parent and cleared reference do not invalidate an explicitly marked Parent.</summary>
    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "none")]
    [InlineData(false, "target")]
    [InlineData(true, "target")]
    [InlineData(false, "null")]
    [InlineData(true, "null")]
    public async Task DetachedOldParentMovePreservesMarkedIntentAndRejectsUnplannedCallbacks(
        bool automaticDetection,
        string callbackChange
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using (var setup = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            await setup.NestedSet<NavigationNode>().ForScope(1)
                .InsertChildAsync(new NavigationNode { Id = 4, Name = "Charlie" }, 1, CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback, probe);

        var root = await context.Set<NavigationNode>().Include(node => node.Children)
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        var child = root.Children.Single(node => node.Id == 2);
        var target = root.Children.Single(node => node.Id == 3);
        var unexpected = root.Children.Single(node => node.Id == 4);
        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var rootEntry = context.Entry(root);
        var parent = context.Entry(child).Property(node => node.ParentId);
        var noteValue = context.Entry(note).Property(value => value.Value);
        rootEntry.State = EntityState.Detached;
        child.Parent = null;
        child.ParentId = target.Id;
        parent.IsModified = true;
        noteValue.CurrentValue = "detached parent payload";
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var storedBefore = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            switch (callbackChange)
            {
                case "none":
                    break;
                case "target":
                    child.Parent = unexpected;
                    break;
                case "null":
                    // WHY: An initially cleared reference does not authorize clearing the planned scalar Parent.
                    child.ParentId = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(callbackChange));
            }
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadPreparedOwnerStoreAsync(database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        if (callbackChange == "none")
        {
            Assert.Null(error);
            Assert.Equal(target.Id, storedAfter.Nodes.Single(node => node.Id == child.Id).ParentId);
            Assert.Equal("detached parent payload", storedAfter.Note);
        }
        else
        {
            Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(error).Code);
            Assert.Equal(0, probe.Writes);
            Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
            Assert.Equal(storedBefore.Note, storedAfter.Note);
        }

        Assert.Equal(EntityState.Detached, rootEntry.State);
        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
