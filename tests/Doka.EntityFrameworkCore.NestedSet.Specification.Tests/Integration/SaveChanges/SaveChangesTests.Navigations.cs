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
    public async Task DetachedChildRemovalPreservesOrdinarySaveSemantics(
        bool wrapped,
        bool automaticDetection,
        bool synchronous,
        bool explicitDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateNavigationContextAsync(database, wrapped,
            cancellationToken: TestContext.Current.CancellationToken);

        var root = await context.Set<NavigationNode>().Include(node => node.Children)
            .SingleAsync(node => node.Id == 1, CancellationToken.None);

        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        var child = Assert.Single(root.Children, node => node.Id == 2);
        context.Entry(child).State = EntityState.Detached;
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
        var saved = synchronous ? context.SaveChanges() : await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedNotes = await verification.Set<NavigationNote>().OrderBy(value => value.Id)
            .Select(value => value.Value).ToArrayAsync(CancellationToken.None);

        var storedParent = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        // Assert
        var detectsPayload = automaticDetection || explicitDetection;
        Assert.Equal(detectsPayload ? 2 : 1, saved);
        Assert.Equal([detectsPayload ? "ordinary change" : "original", "original"], storedNotes);
        Assert.Equal(1, storedParent);
        Assert.DoesNotContain(child, root.Children);
        Assert.Equal(EntityState.Detached, context.Entry(child).State);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Accepts equivalent untracked principals while rejecting an actual change to the scope key.</summary>
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
    public async Task ScopePrincipalReplacementUsesKeyIdentity(
        bool wrapped,
        bool automaticDetection,
        bool synchronous,
        bool sameKey
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var node = await context.Set<NavigationNode>().SingleAsync(value => value.Id == 2, CancellationToken.None);
        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        var otherScope = await context.Set<NavigationScopeOwner>().Where(value => value.Id != node.Scope)
            .Select(value => value.Id).SingleAsync(CancellationToken.None);

        var before = (node.Scope, node.ParentId, node.TreeId, node.Left, node.Right, node.Depth, node.Position);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        node.ScopeOwner = new NavigationScopeOwner { Id = sameKey ? node.Scope : otherScope, Value = "owner" };
        note.Value = "ordinary change";

        // Act
        var failure = await Record.ExceptionAsync(() => SaveNavigationAsync(context, synchronous,
            cancellationToken: TestContext.Current.CancellationToken));

        await using var verification = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var after = await verification.Set<NavigationNode>().Where(value => value.Id == 2)
            .Select(value => new
            {
                value.Scope,
                value.ParentId,
                value.TreeId,
                value.Left,
                value.Right,
                value.Depth,
                value.Position,
            }).SingleAsync(CancellationToken.None);

        var storedNote = await verification.Set<NavigationNote>().Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var storedOtherOwner = await verification.Set<NavigationScopeOwner>().Where(value => value.Id == otherScope)
            .Select(value => value.Value).SingleAsync(CancellationToken.None);

        // Assert
        if (sameKey)
        {
            Assert.Null(failure);
        }
        else
        {
            AssertScopeChangeRejected(failure, context, wrapped, synchronous, automaticDetection);
            Assert.Equal(0, probe.Writes);
        }

        Assert.Equal(before, (after.Scope, after.ParentId, after.TreeId, after.Left, after.Right,
            after.Depth, after.Position));

        Assert.Equal(sameKey && automaticDetection ? "ordinary change" : "original", storedNote);
        Assert.Equal("other owner", storedOtherOwner);
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
    public async Task DeferredPrincipalCascadeCannotDeleteTrackedHierarchy(
        bool wrapped,
        bool automaticDetection,
        bool synchronous
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var owner = await context.Set<NavigationScopeOwner>().Include(value => value.Nodes)
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        var before = owner.Nodes.OrderBy(node => node.Id).Select(node => new
        {
            node.Id,
            node.Scope,
            node.ParentId,
            node.TreeId,
            node.Left,
            node.Right,
            node.Depth,
            node.Position,
        }).ToArray();

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        context.ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        context.Remove(owner);
        var statesBeforeSave = context.GetService<IUpdateAdapterFactory>().Create().Entries
            .Where(entry => entry.EntityType.ClrType == typeof(NavigationNode))
            .Select(entry => entry.EntityState).ToArray();

        // Act
        var failure = await Record.ExceptionAsync(() => SaveNavigationAsync(context, synchronous,
            cancellationToken: TestContext.Current.CancellationToken));

        await using var verification = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var after = await verification.Set<NavigationNode>().OrderBy(node => node.Id).Select(node => new
        {
            node.Id,
            node.Scope,
            node.ParentId,
            node.TreeId,
            node.Left,
            node.Right,
            node.Depth,
            node.Position,
        }).ToArrayAsync(CancellationToken.None);

        var owners = await verification.Set<NavigationScopeOwner>().CountAsync(CancellationToken.None);
        var storedNote = await verification.Set<NavigationNote>().Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(3, statesBeforeSave.Length);
        Assert.All(statesBeforeSave, state => Assert.Equal(EntityState.Unchanged, state));
        Assert.Equal(before, after);
        Assert.Equal(2, owners);
        Assert.Equal("original", storedNote);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Explicit hierarchy work does not detect accompanying raw payload in manual mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualHierarchyWorkPreservesUnmarkedPayload(bool facade)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        var nodeQuery = context.Set<NavigationNode>().AsQueryable();

        if (facade)
        {
            // WHY: Explicit facade mutations use the default rejection policy for affected tracked hierarchy rows.
            nodeQuery = nodeQuery.AsNoTracking();
        }

        var node = await nodeQuery.SingleAsync(value => value.Id == 2, CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        note.Value = "accompanying payload";

        if (!facade)
        {
            node.ParentId = 3;
            context.Entry(node).Property(value => value.ParentId).IsModified = true;
        }

        // Act
        if (facade)
        {
            await context.NestedSet<NavigationNode>().ForScope(node.Scope)
                .InsertChildAsync(new NavigationNode { Id = 4, Name = "Delta" }, 1, CancellationToken.None);
        }
        else
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var verification = await CreateNavigationContextAsync(database, wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedNote = await verification.Set<NavigationNote>().Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var storedParent = await verification.Set<NavigationNode>().Where(value => value.Id == 2)
            .Select(value => value.ParentId).SingleAsync(CancellationToken.None);

        var nodes = await verification.Set<NavigationNode>().CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal("original", storedNote);
        Assert.Equal(facade ? 1 : 3, storedParent);
        Assert.Equal(facade ? 4 : 3, nodes);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
