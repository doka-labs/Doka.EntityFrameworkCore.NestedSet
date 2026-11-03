namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>A marked null parent still requires an explicit new TreeId despite synchronized navigations.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkedNullParentWithSynchronizedNavigationsRequiresExplicitTreeId(bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, probe);
        var child = await PreparePlannedRootAsync(context, automaticDetection);
        var before = CapturePlannedParentState(context);
        var storedBefore = await ReadPreparedOwnerStoreAsync(database);

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var storedAfter = await ReadPreparedOwnerStoreAsync(database);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(error);
        Assert.Equal(NestedSetErrorCode.OperationRejected, rejection.Code);
        Assert.Equal("Setting Parent to null requires DetachAsTreeAsync with an explicit new TreeId.",
            rejection.Message);

        Assert.Equal(0, probe.Writes);
        AssertPlannedParentState(before, after);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal("original", storedAfter.Note);
        Assert.Equal(1, storedAfter.Nodes.Single(node => node.Id == child.Id).ParentId);
        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Marks an independent-root request while preserving the caller's explicit navigation changes.</summary>
    private static async Task<NavigationNode> PreparePlannedRootAsync(DbContext context, bool automaticDetection)
    {
        var nodes = await context.Set<NavigationNode>().Include(node => node.Children).Include(node => node.ScopeOwner)
            .ToArrayAsync(CancellationToken.None);

        var child = nodes.Single(node => node.Id == 2);
        var root = nodes.Single(node => node.Id == 1);
        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        // WHY: Initialize native dependent indexes before changing their CLR FK input or capturing rollback state.
        _ = CapturePlannedParentState(context);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var parent = context.Entry(child).Property(node => node.ParentId);
        var noteValue = context.Entry(note).Property(value => value.Value);
        child.ParentId = null;
        parent.IsModified = true;
        child.Parent = null;
        root.Children.Remove(child);
        noteValue.CurrentValue = "root payload";
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (automaticDetection)
        {
            // WHY: Automatic rollback restores the detected baseline, matching the other parent-move cases.
            context.ChangeTracker.DetectChanges();
        }

        return child;
    }
}
