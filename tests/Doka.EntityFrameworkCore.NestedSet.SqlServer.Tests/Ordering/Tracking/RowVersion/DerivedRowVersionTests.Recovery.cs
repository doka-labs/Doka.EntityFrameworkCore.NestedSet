namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

public sealed partial class DerivedRowVersionTests
{
    /// <summary>A payload callback failure restores both persisted and tracked tokens before parent movement.</summary>
    [Fact]
    public async Task SaveNestedSetChangesAsync_LateFailure_RestoresDerivedTokensAndPendingPayload()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            interceptor: null);
        var tracked = await SeedAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var changed = Assert.IsType<DerivedVersionTokenNode>(tracked.Single(node => node.Id == 2));
        var peer = tracked.Single(node => node.Id == 3);
        var tokens = tracked
            .OfType<DerivedVersionTokenNode>()
            .ToDictionary(node => node.Id, node => node.Version.ToArray());

        var before = await SnapshotAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        changed.ParentId = 5;
        changed.Name = "Zulu";
        peer.Name = "Zulu peer";
        var callbackRan = false;

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveNestedSetChangesAsync(
            false,
            async token =>
            {
                await context.SaveChangesAsync(false, token);
                callbackRan = true;

                throw new InvalidOperationException("Derived token rollback probe.");
            },
            CancellationToken.None));

        // Assert
        Assert.True(callbackRan);
        Assert.Equal("Derived token rollback probe.", failure.Message);
        Assert.Equal(before, await SnapshotAsync(context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(
            tracked.OfType<DerivedVersionTokenNode>(),
            node =>
            {
                Assert.Equal(
                    tokens[node.Id],
                    context
                        .Entry(node)
                        .Property(value => value.Version)
                        .CurrentValue);

                Assert.Equal(
                    tokens[node.Id],
                    context
                        .Entry(node)
                        .Property(value => value.Version)
                        .OriginalValue);
            });

        Assert.Equal(5, changed.ParentId);
        Assert.Equal("Zulu", changed.Name);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(changed)
                .State);

        Assert.Equal("Zulu peer", peer.Name);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(peer)
                .State);

        await AssertValidAsync(context, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>A failed bulk refresh restores mixed-subtype token inputs without retaining entries.</summary>
    [Fact]
    public async Task InsertSubtreeAsync_FailedBulkInsertion_RestoresDerivedTokenInputs()
    {
        // Arrange
        var probe = new RefreshProbe { FailBulkRefresh = true };
        await using var context = await _fixture.ResetAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            interceptor: probe);

        var hierarchy = context
            .NestedSet<DerivedVersionBaseNode>()
            .ForScope(1);

        await hierarchy.InsertRootAsync(
            new DerivedVersionBaseNode
            {
                Id = 1,
                Name = "Root",
            },
            Guid.Empty,
            CancellationToken.None);

        var input = new DerivedVersionTokenNode
        {
            Id = 10,
            Name = "Alpha",
            Version = [1, 2, 3],
        };

        var child = new DerivedVersionTokenNode
        {
            Id = 12,
            Name = "Child",
            Version = [4, 5, 6],
        };

        var inputToken = input.Version.ToArray();
        var childToken = child.Version.ToArray();
        var branch = new NestedSetBranch<DerivedVersionBaseNode>(
            input,
            [
                new NestedSetBranch<DerivedVersionBaseNode>(
                    new DerivedVersionPlainNode
                    {
                        Id = 11,
                        Name = "Plain",
                    }),
                new NestedSetBranch<DerivedVersionBaseNode>(child),
            ]);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchy.InsertSubtreeAsync(
            branch,
            1,
            CancellationToken.None));

        // Assert
        Assert.True(probe.BulkRefreshAttempted);
        Assert.Equal("Derived token bulk refresh probe.", failure.Message);
        Assert.Equal(inputToken, input.Version);
        Assert.Equal(childToken, child.Version);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(
            1,
            await context
                .Set<DerivedVersionBaseNode>()
                .CountAsync(CancellationToken.None));

        Assert.True(
            (await hierarchy
                .InTree(Guid.Empty)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>A final refresh failure restores tokens already updated by the earlier parent-move refresh.</summary>
    [Fact]
    public async Task SaveNestedSetChangesAsync_FailureAfterParentRefresh_RestoresDerivedTokens()
    {
        // Arrange
        var probe = new RefreshProbe { FailAfterFirstRead = true };
        await using var context = await _fixture.ResetAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            interceptor: probe);

        var tracked = await SeedAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        var changed = Assert.IsType<DerivedVersionTokenNode>(tracked.Single(node => node.Id == 2));
        var peer = tracked.Single(node => node.Id == 3);
        var tokens = tracked
            .OfType<DerivedVersionTokenNode>()
            .ToDictionary(node => node.Id, node => node.Version.ToArray());

        var before = await SnapshotAsync(context, cancellationToken: TestContext.Current.CancellationToken);
        changed.ParentId = 5;
        changed.Name = "Zulu";
        peer.Name = "Zulu peer";

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveNestedSetChangesAsync(
            false,
            async token =>
            {
                var count = await context.SaveChangesAsync(false, token);
                probe.Active = true;

                return count;
            },
            CancellationToken.None));

        // Assert
        probe.Active = false;
        Assert.Equal("Derived token late refresh probe.", failure.Message);
        Assert.Equal(2, probe.Commands.Count);
        Assert.Equal(before, await SnapshotAsync(context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(
            tracked.OfType<DerivedVersionTokenNode>(),
            node =>
            {
                Assert.Equal(
                    tokens[node.Id],
                    context
                        .Entry(node)
                        .Property(value => value.Version)
                        .CurrentValue);

                Assert.Equal(
                    tokens[node.Id],
                    context
                        .Entry(node)
                        .Property(value => value.Version)
                        .OriginalValue);
            });

        Assert.Equal(5, changed.ParentId);
        Assert.Equal("Zulu", changed.Name);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(changed)
                .State);

        Assert.Equal("Zulu peer", peer.Name);
        Assert.Equal(
            EntityState.Modified,
            context.Entry(peer)
                .State);

        await AssertValidAsync(context, cancellationToken: TestContext.Current.CancellationToken);
    }
}
