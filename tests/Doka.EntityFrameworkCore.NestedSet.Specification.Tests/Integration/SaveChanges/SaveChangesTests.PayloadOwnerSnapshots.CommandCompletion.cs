namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>A command-completion mutation cannot replace the FK values sent by the original SQL update.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_PayloadOwnerSnapshot_ExcludesCommandCompletionMutation(
        bool coordinated
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPayloadOwnersAsync(
            database,
            assigned: true,
            cancellationToken: TestContext.Current.CancellationToken);

        await using (var setup = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            await setup.AddAsync(
                new NavigationPayloadOwner
                {
                    Scope = 1,
                    Id = 3,
                },
                CancellationToken.None);

            await setup.SaveChangesAsync(CancellationToken.None);
        }

        var callback = new PayloadOwnerCommandCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            probe);

        var node = await context
            .Set<NavigationNode>()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        var owners = await context
            .Set<NavigationPayloadOwner>()
            .Include(value => value.Nodes)
            .Where(value => value.Scope == node.Scope)
            .ToArrayAsync(CancellationToken.None);

        var target = owners.Single(value => value.Id == 2);
        var callbackTarget = owners.Single(value => value.Id == 3);
        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var nodeEntry = context.Entry(node);
        var name = nodeEntry.Property(value => value.Name);
        var payloadOwner = nodeEntry.Property(value => value.PayloadOwnerId);
        var noteValue = context
            .Entry(note)
            .Property(value => value.Value);

        if (coordinated)
        {
            node.Name = "Zulu";
            name.IsModified = true;
        }

        node.PayloadOwnerId = target.Id;
        payloadOwner.IsModified = true;
        target.Nodes.Add(node);
        callback.OnUpdate = () =>
        {
            node.PayloadOwnerId = callbackTarget.Id;
            callbackTarget.Nodes.Add(node);
        };

        // WHY: This first save sends owner2, but its command callback changes the live CLR value before EF returns.
        await context.SaveChangesAsync(CancellationToken.None);
        var savedSnapshot = PayloadOwnerSnapshotContains(context, target, node);
        var callbackSnapshot = PayloadOwnerSnapshotContains(context, callbackTarget, node);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedAfterSave = await verification
            .Set<NavigationNode>()
            .Where(value => value.Id == node.Id)
            .Select(value => value.PayloadOwnerId)
            .SingleAsync(CancellationToken.None);

        nodeEntry.State = EntityState.Detached;
        noteValue.CurrentValue = "following payload";
        var writesBeforeFollowup = probe.Writes;

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var storedOwner = await verification
            .Set<NavigationNode>()
            .Where(value => value.Id == node.Id)
            .Select(value => value.PayloadOwnerId)
            .SingleAsync(CancellationToken.None);

        var validation = await verification
            .NestedSet<NavigationNode>()
            .ForScope(node.Scope)
            .InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.True(callback.Reached);
        Assert.Equal(2, storedAfterSave);
        Assert.Equal(3, node.PayloadOwnerId);
        Assert.True(savedSnapshot);
        Assert.False(callbackSnapshot);
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(error)
                .Code);

        Assert.Equal(writesBeforeFollowup, probe.Writes);
        Assert.Equal("original", storedNote);
        Assert.Equal(2, storedOwner);
        Assert.Empty(validation.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
