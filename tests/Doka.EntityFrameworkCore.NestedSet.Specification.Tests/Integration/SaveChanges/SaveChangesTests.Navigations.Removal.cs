using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Removing a detached child leaves ordinary saves usable without changing manual detection.</summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    public async Task SaveChanges_DetachedChildRemoval_PreservesOrdinarySaveSemantics(
        bool wrapped,
        bool automaticDetection,
        bool synchronous,
        bool explicitDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped,
            cancellationToken: TestContext.Current.CancellationToken);

        var root = await context
            .Set<NavigationNode>()
            .Include(node => node.Children)
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(CancellationToken.None);

        var child = Assert.Single(root.Children, node => node.Id == 2);
        context.Entry(child)
            .State = EntityState.Detached;

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        root.Children.Remove(child);
        note.Value = "ordinary change";

        if (explicitDetection)
        {
            // WHY: EF can retain detached members in relationship snapshots even after an explicit scan.
            context.ChangeTracker.DetectChanges();
        }

        await context.AddAsync(new NavigationNote { Id = 2 }, CancellationToken.None);

        // Act
        // WHY: The synchronous variants exercise the explicitly synchronous application save boundary.
        // ReSharper disable once MethodHasAsyncOverload
        var saved = synchronous
            ? context.SaveChanges()
            : await context.SaveChangesAsync(CancellationToken.None);

        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedNotes = await verification
            .Set<NavigationNote>()
            .OrderBy(value => value.Id)
            .Select(value => value.Value)
            .ToArrayAsync(CancellationToken.None);

        var storedParent = await verification
            .Set<NavigationNode>()
            .Where(node => node.Id == 2)
            .Select(node => node.ParentId)
            .SingleAsync(CancellationToken.None);

        // Assert
        var detectsPayload = automaticDetection || explicitDetection;
        Assert.Equal(
            detectsPayload
                ? 2
                : 1,
            saved);

        Assert.Equal(
            [
                detectsPayload
                    ? "ordinary change"
                    : "original",
                "original",
            ],
            storedNotes);

        Assert.Equal(1, storedParent);
        Assert.DoesNotContain(child, root.Children);
        Assert.Equal(
            EntityState.Detached,
            context.Entry(child)
                .State);

        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     Rejects hierarchy deletion introduced by deferred principal cascades before any database write.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveNavigationAsync_DeferredPrincipalCascade_CannotDeleteTrackedHierarchy(
        bool wrapped,
        bool automaticDetection,
        bool synchronous
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var owner = await context
            .Set<NavigationScopeOwner>()
            .Include(value => value.Nodes)
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        var before = owner
            .Nodes
            .OrderBy(node => node.Id)
            .Select(node => new
            {
                node.Id,
                node.Scope,
                node.ParentId,
                node.TreeId,
                node.Left,
                node.Right,
                node.Depth,
                node.Position,
            })
            .ToArray();

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        context.Remove(owner);
        var statesBeforeSave = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Where(entry => entry.EntityType.ClrType == typeof(NavigationNode))
            .Select(entry => entry.EntityState)
            .ToArray();

        // Act
        var failure = await Record.ExceptionAsync(() => SaveNavigationAsync(
            context,
            synchronous,
            cancellationToken: TestContext.Current.CancellationToken));

        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var after = await verification
            .Set<NavigationNode>()
            .OrderBy(node => node.Id)
            .Select(node => new
            {
                node.Id,
                node.Scope,
                node.ParentId,
                node.TreeId,
                node.Left,
                node.Right,
                node.Depth,
                node.Position,
            })
            .ToArrayAsync(CancellationToken.None);

        var owners = await verification
            .Set<NavigationScopeOwner>()
            .CountAsync(CancellationToken.None);

        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(0, probe.Writes);
        Assert.Equal(3, statesBeforeSave.Length);
        Assert.All(statesBeforeSave, state => Assert.Equal(EntityState.Unchanged, state));
        Assert.Equal(before, after);
        Assert.Equal(2, owners);
        Assert.Equal("original", storedNote);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
