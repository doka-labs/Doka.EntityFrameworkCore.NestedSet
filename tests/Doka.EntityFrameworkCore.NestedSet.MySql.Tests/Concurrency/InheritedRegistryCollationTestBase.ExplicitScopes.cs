namespace Doka.EntityFrameworkCore.NestedSet.Tests.MySql;

public abstract partial class InheritedRegistryCollationTestBase
{
    /// <summary>Explicit case-insensitive Scope comparison deduplicates locks despite a binary table default.</summary>
    [Fact]
    public async Task AcquireAsync_ExplicitScopeCollation_DeduplicatesExistingAliasLocks()
    {
        // Arrange
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync<ExplicitRegistryScopeCollationContext>(
            Engine,
            static options => new ExplicitRegistryScopeCollationContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var scope = new BroadScope($"REGISTRY-OVERRIDE-{root}");
        var alias = new BroadScope(scope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var before = await SeedRootAsync(
            context,
            scope,
            treeId,
            root,
            "Existing root",
            cancellationToken: TestContext.Current.CancellationToken);

        var requests = Requests(context, [scope, alias], treeId, NestedSetTreeLockMode.Existing);
        ClearCaptures(probe);

        // Act
        await AcquireAsync(context, requests, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new[] { scope.Value }, LockScopes(probe));
        Assert.Equal(
            before,
            await ReadRootAsync(
                context,
                scope,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            before,
            await ReadRootAsync(
                context,
                alias,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            1,
            await RegistryCountAsync(
                context,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Equivalent Scope aliases cannot reserve a second root under the same TreeId.</summary>
    [Fact]
    public async Task InsertRootAsync_ExplicitScopeCollation_RejectsDuplicateTreeReservation()
    {
        // Arrange
        await using var context = await _fixture.CreateContextAsync<ExplicitRegistryScopeCollationContext>(
            Engine,
            static options => new ExplicitRegistryScopeCollationContext(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var scope = new BroadScope($"REGISTRY-DUPLICATE-{root}");
        var alias = new BroadScope(scope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var before = await SeedRootAsync(
            context,
            scope,
            treeId,
            root,
            "Existing root",
            cancellationToken: TestContext.Current.CancellationToken);

        var rejected = new BroadScopeNode
        {
            Id = root + 10,
            Name = "Rejected root",
        };

        var rejectedBefore = Snapshot(rejected);

        // Act
        var failure = await Record.ExceptionAsync(() => context
            .NestedSet<BroadScopeNode>()
            .ForScope(alias)
            .InsertRootAsync(rejected, treeId, CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.TreeIdUnavailable,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Equal(rejectedBefore, Snapshot(rejected));
        Assert.Equal(
            EntityState.Detached,
            context.Entry(rejected)
                .State);

        Assert.Equal(
            before,
            await ReadRootAsync(
                context,
                scope,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            before,
            await ReadRootAsync(
                context,
                alias,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            1,
            await RegistryCountAsync(
                context,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }

    /// <summary>Native forest validation rejects equivalent Scope and TreeId aliases before any reservation.</summary>
    [Fact]
    public async Task RequireDistinctAsync_ExplicitScopeCollation_RejectsDuplicateImportedTreeIdentities()
    {
        // Arrange
        var probe = new EnterpriseProbe();
        await using var context = await _fixture.CreateContextAsync<ExplicitRegistryScopeCollationContext>(
            Engine,
            static options => new ExplicitRegistryScopeCollationContext(options),
            cancellationToken: TestContext.Current.CancellationToken,
            probe);

        var root = Interlocked.Add(ref s_nextRoot, 1000);
        var scope = new BroadScope($"REGISTRY-IMPORT-{root}");
        var alias = new BroadScope(scope.Value.ToLowerInvariant());
        var treeId = Guid.NewGuid();
        var requests = Requests(context, [scope, alias], treeId, NestedSetTreeLockMode.New);
        ClearCaptures(probe);

        // Act
        var failure = await Record.ExceptionAsync(() => NestedSetTreeLocks.RequireDistinctAsync(
            context,
            requests,
            CancellationToken.None));

        // Assert
        Assert.Equal(
            NestedSetErrorCode.InvalidImport,
            Assert.IsType<NestedSetException>(failure)
                .Code);

        Assert.Single(probe.Commands);
        Assert.Empty(LockScopes(probe));
        Assert.Empty(
            await context
                .NestedSet<BroadScopeNode>()
                .ForScope(scope)
                .InTree(treeId)
                .Nodes
                .ToArrayAsync(CancellationToken.None));

        Assert.Equal(
            0,
            await RegistryCountAsync(
                context,
                treeId,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
    }
}
