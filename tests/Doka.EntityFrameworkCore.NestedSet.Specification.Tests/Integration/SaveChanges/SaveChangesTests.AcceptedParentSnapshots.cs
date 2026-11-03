using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Rollback preserves a third parent's stale membership accepted by EF's ordinary lifecycle.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedParentMovePreservesAcceptedStalePrincipalMembership(bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();

        await using (var setup = await CreateNavigationContextAsync(database, wrapped: true))
        {
            var tree = setup.NestedSet<NavigationNode>().ForScope(1);
            await tree.InsertSubtreeAsync(
                new NestedSetBranch<NavigationNode>(new NavigationNode { Id = 4, Name = "Charlie" },
                [
                    new NestedSetBranch<NavigationNode>(new NavigationNode { Id = 5, Name = "Delta" }),
                    new NestedSetBranch<NavigationNode>(new NavigationNode { Id = 6, Name = "Echo" }),
                ]), 1, CancellationToken.None);

            await tree.MoveToAsync(2, 4, CancellationToken.None);
        }

        await using var context = await CreateNavigationContextAsync(database, wrapped: true);
        var nodes = await context.Set<NavigationNode>().Include(node => node.Children).Include(node => node.ScopeOwner)
            .ToArrayAsync(CancellationToken.None);

        var child = nodes.Single(node => node.Id == 2);
        var root = nodes.Single(node => node.Id == 1);
        var target = nodes.Single(node => node.Id == 3);
        var third = nodes.Single(node => node.Id == 4);
        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        // WHY: Detachment can leave a principal's CLR collection and native relationship snapshot populated.
        context.Entry(child).State = EntityState.Detached;

        await using (var external = await CreateNavigationContextAsync(database, wrapped: true))
        {
            await external.NestedSet<NavigationNode>().ForScope(1).MoveToAsync(2, 1, CancellationToken.None);
        }

        child.ParentId = root.Id;
        child.Parent = root;
        context.Attach(child);
        await context.Entry(child).ReloadAsync(CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        var accepted = await context.SaveChangesAsync(CancellationToken.None);
        var updates = context.GetService<IUpdateAdapterFactory>().Create();
        var thirdEntry = updates.Entries.Single(entry => ReferenceEquals(entry.ToEntityEntry().Entity, third));
        var children = thirdEntry.EntityType.FindNavigation(nameof(NavigationNode.Children))!;
        var staleClrAccepted = third.Children.Contains(child);
        var staleSnapshotAccepted = thirdEntry.GetRelationshipSnapshotValue(children) is IEnumerable<object> members
            && members.Any(member => ReferenceEquals(member, child));

        // WHY: Warm dependent maps before preparing the next raw FK edit; capturing evidence must not create them.
        _ = CapturePlannedParentState(context);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var parent = context.Entry(child).Property(node => node.ParentId);
        var noteValue = context.Entry(note).Property(value => value.Value);
        child.ParentId = target.Id;
        parent.IsModified = true;
        child.Parent = target;
        noteValue.CurrentValue = "planned payload";
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (automaticDetection)
        {
            // WHY: Automatic save rollback restores the already detected application baseline.
            context.ChangeTracker.DetectChanges();
        }

        var before = CapturePlannedParentState(context);
        var failure = new InvalidOperationException("Injected stale-principal rollback failure.");
        context.SavedChanges += (_, _) => throw failure;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var stored = await ReadPreparedOwnerStoreAsync(database);

        // Assert
        Assert.Equal(0, accepted);
        Assert.True(staleClrAccepted);
        Assert.True(staleSnapshotAccepted);
        Assert.Same(failure, error);
        AssertPlannedParentState(before, after);
        Assert.Contains(child, third.Children);
        Assert.Equal([2, 5, 6], third.Children.Select(node => node.Id).Order());
        Assert.Equal(1, stored.Nodes.Single(node => node.Id == 2).ParentId);
        Assert.Equal("original", stored.Note);
        Assert.Equal(0, stored.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
