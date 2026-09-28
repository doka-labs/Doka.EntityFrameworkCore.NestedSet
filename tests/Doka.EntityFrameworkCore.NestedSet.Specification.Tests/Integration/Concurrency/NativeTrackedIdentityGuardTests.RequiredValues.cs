namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Rejects invalid required tracked identities before creating any transaction or database command.</summary>
public abstract partial class NativeTrackedIdentityGuardTests
{
    /// <summary>An unchanged attached entity cannot hide a null required reference TreeId.</summary>
    [Fact]
    public async Task NullRequiredTrackedTreeIdRejectsBeforeDatabaseCommands()
    {
        // Arrange
        var probe = new NativeGuardProbe();
        await using var context = await CreateAliasContextAsync(Engine, probe);
        var root = NativeTrackedIdentityGuardTestSupport.NextRoot(1000);
        var affected = Guid
            .NewGuid()
            .ToString("N");

        await SeedTreeAsync(context, affected, root, 1);
        await SeedTreeAsync(context, Guid.NewGuid().ToString("N"), root + 10, 1);
        var node = await ReadNodeAsync(context, root + 11);
        var tracked = context.Entry(node);
        tracked.Property(nameof(BroadTreeIdNode.TreeId)).CurrentValue = null;
        context.Attach(node);
        probe.Armed = true;

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadTreeIdNode>()
            .DeleteTreeAsync(new BroadTreeId(affected), CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, probe.Commands);
        Assert.Equal(EntityState.Unchanged, tracked.State);
        Assert.Null(context.Database.CurrentTransaction);
    }
}
