namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
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
    public async Task SaveNavigationAsync_ScopePrincipalReplacement_UsesKeyIdentity(
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
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped,
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var node = await context
            .Set<NavigationNode>()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(CancellationToken.None);

        var otherScope = await context
            .Set<NavigationScopeOwner>()
            .Where(value => value.Id != node.Scope)
            .Select(value => value.Id)
            .SingleAsync(CancellationToken.None);

        var before = (node.Scope, node.ParentId, node.TreeId, node.Left, node.Right, node.Depth, node.Position);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        node.ScopeOwner = new NavigationScopeOwner
        {
            Id = sameKey
                ? node.Scope
                : otherScope,
            Value = "owner",
        };

        note.Value = "ordinary change";

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
            .Where(value => value.Id == 2)
            .Select(value => new
            {
                value.Scope,
                value.ParentId,
                value.TreeId,
                value.Left,
                value.Right,
                value.Depth,
                value.Position,
            })
            .SingleAsync(CancellationToken.None);

        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var storedOtherOwner = await verification
            .Set<NavigationScopeOwner>()
            .Where(value => value.Id == otherScope)
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

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

        Assert.Equal(
            before,
            (after.Scope, after.ParentId, after.TreeId, after.Left, after.Right,
                after.Depth, after.Position));

        Assert.Equal(
            sameKey && automaticDetection
                ? "ordinary change"
                : "original",
            storedNote);

        Assert.Equal("other owner", storedOtherOwner);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Explicit hierarchy work does not detect accompanying raw payload in manual mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertChildAsync_ManualHierarchyWork_PreservesUnmarkedPayload(
        bool facade
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(CancellationToken.None);

        var nodeQuery = context
            .Set<NavigationNode>()
            .AsQueryable();

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
            context
                .Entry(node)
                .Property(value => value.ParentId)
                .IsModified = true;
        }

        // Act
        if (facade)
        {
            await context
                .NestedSet<NavigationNode>()
                .ForScope(node.Scope)
                .InsertChildAsync(
                    new NavigationNode
                    {
                        Id = 4,
                        Name = "Delta",
                    },
                    1,
                    CancellationToken.None);
        }
        else
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedNote = await verification
            .Set<NavigationNote>()
            .Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var storedParent = await verification
            .Set<NavigationNode>()
            .Where(value => value.Id == 2)
            .Select(value => value.ParentId)
            .SingleAsync(CancellationToken.None);

        var nodes = await verification
            .Set<NavigationNode>()
            .CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal("original", storedNote);
        Assert.Equal(
            facade
                ? 1
                : 3,
            storedParent);

        Assert.Equal(
            facade
                ? 4
                : 3,
            nodes);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
