namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>Rechecks changes or new tracked aliases introduced while a native guard read is awaiting.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteTreeAsync_NativeProbe_CannotIntroduceUnplannedTrackerChanges(
        bool attachAffectedAlias
    )
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affectedTree = $"guard-callback-{root}";
        var otherTree = $"guard-unaffected-{root}";
        await SeedTreeAsync(context, affectedTree, root, 1, cancellationToken: TestContext.Current.CancellationToken);
        await SeedTreeAsync(context, otherTree, root + 10, 1, cancellationToken: TestContext.Current.CancellationToken);
        var affected = await ReadNodeAsync(context, root + 1, cancellationToken: TestContext.Current.CancellationToken);
        affected.TreeId = new BroadTreeId(affectedTree.ToUpperInvariant());
        var other = await ReadNodeAsync(context, root + 11, cancellationToken: TestContext.Current.CancellationToken);
        context.Attach(other);
        probe.BeforeNativeRead = () =>
        {
            if (attachAffectedAlias)
            {
                context.Attach(affected);
            }
            else
            {
                other.Name = "Callback change";
            }
        };

        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(affectedTree), CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(1, probe.NativeReads);
        Assert.Equal(
            2,
            await context
                .Set<BroadTreeIdNode>()
                .AsNoTracking()
                .CountAsync(node => node.Id >= root && node.Id <= root + 1, CancellationToken.None));

        Assert.Equal(
            "Child-000",
            (await ReadNodeAsync(
                context,
                root + 11,
                cancellationToken: TestContext.Current.CancellationToken)).Name);

        Assert.Equal(
            attachAffectedAlias
                ? EntityState.Unchanged
                : EntityState.Modified,
            context.Entry(other)
                .State);
    }

    /// <summary>Rejects hierarchy attachments during registry locking when no hierarchy entries were tracked.</summary>
    [Fact]
    public async Task DeleteTreeAsync_RegistryLock_CannotAttachAnUnplannedAffectedHierarchyEntry()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(
            Engine,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var treeId = $"guard-lock-{root}";
        await SeedTreeAsync(context, treeId, root, 1, cancellationToken: TestContext.Current.CancellationToken);
        var alias = await ReadNodeAsync(context, root + 1, cancellationToken: TestContext.Current.CancellationToken);
        probe.RegistryTable = NestedSetTreeRegistryMapping.For(context.Model.FindEntityType(typeof(BroadTreeIdNode))!)
            .Store.Name;

        probe.AfterRegistryRead = () => context.Attach(alias);
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(treeId), CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidContext,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(1, probe.RegistryCallbacks);
        Assert.Equal(0, probe.NativeReads);
        Assert.Equal(
            EntityState.Unchanged,
            context.Entry(alias)
                .State);

        Assert.Equal(
            2,
            await context
                .NestedSet<BroadTreeIdNode>()
                .InTree(new BroadTreeId(treeId))
                .Nodes
                .CountAsync(CancellationToken.None));
    }
}
