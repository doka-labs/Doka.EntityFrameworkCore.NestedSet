namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class OrderingTrackerTests
{
    /// <summary>
    ///     Restores an unrelated identity's CLR sentinel and temporary key before retrying the same context.
    /// </summary>
    [Fact]
    public async Task LateFailureRestoresAddedIdentityAndAllowsRetry()
    {
        // Arrange
        var database = await CreateDatabaseAsync(Engine);
        var probe = new TrackerFailureProbe();
        await using var context = new TrackerContext(await OptionsAsync(database, probe));
        var node = await LoadNodeAsync(context, 3);
        node.Name = "Zulu";
        var addition = new TrackerAddition();
        await context.AddAsync(addition, CancellationToken.None);
        context.ChangeTracker.DetectChanges();
        var nodeBefore = Capture(context, node);
        var additionBefore = Capture(context, addition);
        var clrIdentityBefore = addition.Id;

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var nodeAfterFailure = Capture(context, node);
        var additionAfterFailure = Capture(context, addition);
        var clrIdentityAfterFailure = addition.Id;
        var completedPayload = probe.CompletedPayloadUpdates;
        var completedInsert = probe.CompletedAdditionInserts;

        await using var verification = new TrackerContext(await OptionsAsync(database));
        var additionsAfterFailure = await verification
            .Set<TrackerAddition>()
            .CountAsync(CancellationToken.None);

        var orderAfterFailure = await ChildIdsAsync(verification);
        probe.FailAfterPayload = false;
        var retry = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var finalOrder = await ChildIdsAsync(verification);
        var persistedAddition = await verification
            .Set<TrackerAddition>()
            .AsNoTracking()
            .SingleOrDefaultAsync(CancellationToken.None);

        // Assert
        Assert.IsType<TrackerInjectedException>(failure);
        Assert.Equal(1, completedPayload);
        Assert.Equal(1, completedInsert);
        Assert.Equal(0, clrIdentityBefore);
        Assert.True(additionBefore.Properties.Single(property => property.Name == "Id").IsTemporary);
        AssertSnapshot(nodeBefore, nodeAfterFailure);
        AssertSnapshot(additionBefore, additionAfterFailure);
        Assert.Equal(clrIdentityBefore, clrIdentityAfterFailure);
        Assert.Equal(0, additionsAfterFailure);
        Assert.Equal([2, 3, 4], orderAfterFailure);
        Assert.Null(retry);
        Assert.Equal([2, 4, 3], finalOrder);
        Assert.NotNull(persistedAddition);
        Assert.True(addition.Id > 0);
        Assert.Equal(addition.Id, persistedAddition.Id);
        Assert.Equal(EntityState.Unchanged, Capture(context, addition).State);
    }

    /// <summary>Preserves a deferred required orphan so a later retry still performs its intended deletion.</summary>
    [Fact]
    public async Task LateFailureRestoresDeferredOrphanAndAllowsRetry()
    {
        // Arrange
        var database = await CreateDatabaseAsync(Engine);
        var probe = new TrackerFailureProbe();
        await using var context = new TrackerContext(await OptionsAsync(database, probe));
        context.ChangeTracker.DeleteOrphansTiming =
            Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming.OnSaveChanges;

        var owner = await context
            .Set<TrackerOwner>()
            .Include(value => value.Children)
            .SingleAsync(CancellationToken.None);

        var child = owner.Children.Single();
        owner.Children.Remove(child);
        var node = await LoadNodeAsync(context, 3);
        node.Name = "Zulu";
        context.ChangeTracker.DetectChanges();
        var childBefore = Capture(context, child);

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var childAfterFailure = Capture(context, child);
        var timingAfterFailure = context.ChangeTracker.DeleteOrphansTiming;
        var navigationAfterFailure = child.Owner;
        var collectionCountAfterFailure = owner.Children.Count;
        var completedPayload = probe.CompletedPayloadUpdates;
        await using var verification = new TrackerContext(await OptionsAsync(database));
        var childrenAfterFailure = await verification
            .Set<TrackerChild>()
            .CountAsync(CancellationToken.None);

        probe.FailAfterPayload = false;
        var retry = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var childrenAfterRetry = await verification
            .Set<TrackerChild>()
            .CountAsync(CancellationToken.None);

        var finalOrder = await ChildIdsAsync(verification);

        // Assert
        Assert.IsType<TrackerInjectedException>(failure);
        Assert.Equal(1, completedPayload);
        AssertSnapshot(childBefore, childAfterFailure);
        Assert.Equal(Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming.OnSaveChanges, timingAfterFailure);
        Assert.Null(navigationAfterFailure);
        Assert.Equal(0, collectionCountAfterFailure);
        Assert.Equal(1, childrenAfterFailure);
        Assert.Null(retry);
        Assert.Equal(0, childrenAfterRetry);
        Assert.Equal([2, 4, 3], finalOrder);
        Assert.Equal(EntityState.Detached, Capture(context, child).State);
    }

    /// <summary>
    ///     Retains the application's Never orphan policy instead of silently converting it into a cascade.
    /// </summary>
    [Fact]
    public async Task RequiredOrphanWithNeverTimingStillFailsWithoutChangingPolicy()
    {
        // Arrange
        var database = await CreateDatabaseAsync(Engine);
        await using var context = new TrackerContext(await OptionsAsync(database));
        context.ChangeTracker.DeleteOrphansTiming = Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming.Never;
        var owner = await context
            .Set<TrackerOwner>()
            .Include(value => value.Children)
            .SingleAsync(CancellationToken.None);

        var child = owner.Children.Single();
        owner.Children.Remove(child);
        var node = await LoadNodeAsync(context, 3);
        node.Name = "Zulu";
        context.ChangeTracker.DetectChanges();
        var before = Capture(context, child);

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = Capture(context, child);
        await using var verification = new TrackerContext(await OptionsAsync(database));
        var childCount = await verification
            .Set<TrackerChild>()
            .CountAsync(CancellationToken.None);

        var order = await ChildIdsAsync(verification);

        // Assert
        Assert.IsType<InvalidOperationException>(failure);
        AssertSnapshot(before, after);
        Assert.Equal(
            Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming.Never,
            context.ChangeTracker.DeleteOrphansTiming);
        Assert.Equal(1, childCount);
        Assert.Null(child.Owner);
        Assert.Empty(owner.Children);
        Assert.Equal([2, 3, 4], order);
    }

    /// <summary>Rejects the first callback-created hierarchy change before SQL while retaining pending edits.</summary>
    [Fact]
    public async Task SavingChangesCallbackCannotBypassInitialOrderingCandidateDiscovery()
    {
        // Arrange
        var database = await CreateDatabaseAsync(Engine);
        var probe = new TrackerFailureProbe { FailAfterPayload = false };
        await using var context = new TrackerContext(await OptionsAsync(database, probe));
        var node = await LoadNodeAsync(context, 3);
        var addition = new TrackerAddition();

        await context.AddAsync(addition, CancellationToken.None);
        context.ChangeTracker.DetectChanges();
        var nodeBefore = Capture(context, node);
        var additionBefore = Capture(context, addition);
        context.SavingChanges += (_, _) => node.Name = "Zulu";

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var nodeAfter = Capture(context, node);
        var additionAfter = Capture(context, addition);
        await using var verification = new TrackerContext(await OptionsAsync(database));
        var additions = await verification
            .Set<TrackerAddition>()
            .CountAsync(CancellationToken.None);

        var order = await ChildIdsAsync(verification);
        var persistedName = await verification
            .Set<TrackerNode>()
            .Where(value => value.Id == 3)
            .Select(value => value.Name)
            .SingleAsync(CancellationToken.None);

        // Assert
        var rejection = Assert.IsAssignableFrom<InvalidOperationException>(failure);
        Assert.Contains("Save callbacks", rejection.Message, StringComparison.Ordinal);
        Assert.Equal(EntityState.Unchanged, nodeBefore.State);
        Assert.Equal(0, probe.CompletedPayloadUpdates);
        Assert.Equal(0, probe.CompletedAdditionInserts);
        Assert.Equal(EntityState.Modified, nodeAfter.State);
        Assert.Equal("Zulu", node.Name);
        AssertSnapshot(additionBefore, additionAfter);
        Assert.Equal(0, addition.Id);
        Assert.Equal(0, additions);
        Assert.Equal("Bravo", persistedName);
        Assert.Equal([2, 3, 4], order);
    }
}
