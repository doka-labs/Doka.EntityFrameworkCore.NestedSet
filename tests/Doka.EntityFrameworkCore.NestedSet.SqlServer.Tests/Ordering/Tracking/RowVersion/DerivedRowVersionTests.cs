namespace Doka.EntityFrameworkCore.NestedSet.Tests.SqlServer;

/// <summary>Protects subtype-only generated tokens after coordinated and explicit structural writes.</summary>
public sealed class DerivedRowVersionTests : ProviderTest,
    IClassFixture<ProviderFixture<DerivedRowVersionFixture, SqlServerEngine>>
{
    private static readonly Guid s_destinationTree = new("d84ef4d1-bbce-47c3-bc67-c9df35bc3524");
    private readonly DerivedRowVersionFixture _fixture;

    /// <summary>Uses the fixture bound to the exact SQL Server engine.</summary>
    /// <param name="fixture">The owner of the independent derived-token hierarchy tables.</param>
    public DerivedRowVersionTests(
        ProviderFixture<DerivedRowVersionFixture, SqlServerEngine> fixture
    ) : base(fixture)
    {
        _fixture = fixture.Value;
    }

    /// <summary>Refreshes changed and unchanged derived tokens without changing caller acceptance semantics.</summary>
    /// <param name="destination">Zero for ordering alone, or the same-tree or cross-tree destination parent.</param>
    /// <param name="acceptAllChanges">Whether the coordinator should accept the caller's pending payload.</param>
    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(5, true)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(6, false)]
    public async Task CoordinatedOwnerRefreshIncludesDerivedTokens(
        int destination,
        bool acceptAllChanges
    )
    {
        // Arrange
        var probe = new RefreshProbe();
        await using var context = await _fixture.ResetAsync(probe);
        var tracked = await SeedAsync(context);
        var changed = Assert.IsType<DerivedVersionTokenNode>(tracked.Single(node => node.Id == 2));
        var leaf = Assert.IsType<DerivedVersionTokenNode>(tracked.Single(node => node.Id == 4));
        var original = changed.Version.ToArray();
        var leafOriginal = leaf.Version.ToArray();
        changed.Name = "Zulu";

        if (destination != 0)
        {
            changed.ParentId = destination;
        }

        // Act
        await context.SaveNestedSetChangesAsync(
            acceptAllChanges,
            async token =>
            {
                var count = await context.SaveChangesAsync(false, token);
                probe.Active = true;

                return count;
            },
            CancellationToken.None);
        probe.Active = false;
        var observed = tracked.OfType<DerivedVersionTokenNode>().ToDictionary(node => node.Id, node =>
        {
            var property = context.Entry(node).Property(value => value.Version);

            return (Current: property.CurrentValue.ToArray(), Original: property.OriginalValue.ToArray(),
                State: context.Entry(node).State);
        });
        var nameOriginal = context.Entry(changed).Property(node => node.Name).OriginalValue;
        var clrToken = changed.Version.ToArray();
        var payloads = tracked.Select(node => node.Payload).ToArray();
        var persisted = await context.Set<DerivedVersionTokenNode>()
            .AsNoTracking().OrderBy(node => node.Id).ToArrayAsync(CancellationToken.None);
        context.ChangeTracker.AcceptAllChanges();
        leaf.Payload = "Later payload";
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(probe.Commands);
        Assert.All(probe.Commands, command =>
        {
            Assert.DoesNotContain("[Payload]", command, StringComparison.Ordinal);
            Assert.DoesNotContain("[Name]", command, StringComparison.Ordinal);
        });
        Assert.NotEqual(leafOriginal, observed[leaf.Id].Current);
        Assert.Equal(observed[leaf.Id].Current, observed[leaf.Id].Original);
        Assert.Equal(EntityState.Unchanged, observed[leaf.Id].State);
        var version = observed[changed.Id];
        Assert.NotEqual(original, version.Current);
        Assert.Equal(acceptAllChanges ? version.Current : original, version.Original);
        Assert.Equal(acceptAllChanges ? version.Current : original, clrToken);
        Assert.Equal(acceptAllChanges ? EntityState.Unchanged : EntityState.Modified, version.State);
        Assert.Equal(acceptAllChanges ? "Zulu" : "Alpha", nameOriginal);
        Assert.All(observed, pair =>
        {
            Assert.Equal(persisted.Single(row => row.Id == pair.Key).Version, pair.Value.Current);

            if (pair.Key != changed.Id)
            {
                Assert.Equal(pair.Value.Current, pair.Value.Original);
                Assert.Equal(EntityState.Unchanged, pair.Value.State);
            }
        });
        Assert.All(payloads, payload => Assert.Equal("Unchanged payload", payload));
        Assert.Equal("Later payload", (await context.Set<DerivedVersionTokenNode>().AsNoTracking()
            .SingleAsync(node => node.Id == leaf.Id, CancellationToken.None)).Payload);
        await AssertValidAsync(context);
    }

    /// <summary>An untracked explicit move permits reloading a derived token for a later payload save.</summary>
    [Fact]
    public async Task UntrackedExplicitMoveAllowsReloadedDerivedTokenPayloadSave()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync();
        var tracked = await SeedAsync(context);
        var leaf = Assert.IsType<DerivedVersionTokenNode>(tracked.Single(node => node.Id == 4));
        var original = leaf.Version.ToArray();
        var hierarchy = context.NestedSet<DerivedVersionBaseNode>().ForScope(1);
        context.ChangeTracker.Clear();

        // Act
        await hierarchy.MoveToAsync(2, 5, CancellationToken.None);
        var persisted = await context.Set<DerivedVersionTokenNode>()
            .SingleAsync(node => node.Id == leaf.Id, CancellationToken.None);
        var afterMove = persisted.Version.ToArray();
        persisted.Payload = "After explicit move";
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.NotEqual(original, afterMove);
        var stored = await context.Set<DerivedVersionTokenNode>().AsNoTracking()
            .SingleAsync(node => node.Id == leaf.Id, CancellationToken.None);
        Assert.Equal("After explicit move", stored.Payload);
        Assert.Equal(stored.Version, persisted.Version);
        Assert.Equal(persisted.Version, context.Entry(persisted).Property(node => node.Version).OriginalValue);
        await AssertValidAsync(context);
    }

    /// <summary>Explicit movement rejects tracked subtypes and retains their tokens and structure.</summary>
    [Fact]
    public async Task TrackedExplicitMoveRejectsDerivedNodesWithoutChangingTokens()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync();
        var tracked = await SeedAsync(context);
        var tokens = tracked.OfType<DerivedVersionTokenNode>()
            .ToDictionary(node => node.Id, node => node.Version.ToArray());
        var before = await SnapshotAsync(context);
        var hierarchy = context.NestedSet<DerivedVersionBaseNode>().ForScope(1);

        // Act
        var rejection = await Assert.ThrowsAsync<NestedSetException>(() =>
            hierarchy.MoveToAsync(2, 5, CancellationToken.None));

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.All(tracked, node => Assert.Equal(EntityState.Unchanged, context.Entry(node).State));
        Assert.All(tracked.OfType<DerivedVersionTokenNode>(), node =>
        {
            Assert.Equal(tokens[node.Id], context.Entry(node).Property(value => value.Version).CurrentValue);
            Assert.Equal(tokens[node.Id], context.Entry(node).Property(value => value.Version).OriginalValue);
        });
        await AssertValidAsync(context);
    }

    /// <summary>A payload callback failure restores both persisted and tracked tokens before parent movement.</summary>
    [Fact]
    public async Task LateFailureRestoresDerivedTokensAndPendingPayload()
    {
        // Arrange
        await using var context = await _fixture.ResetAsync();
        var tracked = await SeedAsync(context);
        var changed = Assert.IsType<DerivedVersionTokenNode>(tracked.Single(node => node.Id == 2));
        var peer = tracked.Single(node => node.Id == 3);
        var tokens = tracked.OfType<DerivedVersionTokenNode>()
            .ToDictionary(node => node.Id, node => node.Version.ToArray());
        var before = await SnapshotAsync(context);
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
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.All(tracked.OfType<DerivedVersionTokenNode>(), node =>
        {
            Assert.Equal(tokens[node.Id], context.Entry(node).Property(value => value.Version).CurrentValue);
            Assert.Equal(tokens[node.Id], context.Entry(node).Property(value => value.Version).OriginalValue);
        });
        Assert.Equal(5, changed.ParentId);
        Assert.Equal("Zulu", changed.Name);
        Assert.Equal(EntityState.Modified, context.Entry(changed).State);
        Assert.Equal("Zulu peer", peer.Name);
        Assert.Equal(EntityState.Modified, context.Entry(peer).State);
        await AssertValidAsync(context);
    }

    /// <summary>A base facade returns the final derived token after single or atomic bulk ordering.</summary>
    /// <param name="bulk">Whether the public operation imports a branch or inserts one leaf.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BaseFacadeInsertionReturnsFreshDerivedTokens(
        bool bulk
    )
    {
        // Arrange
        await using var context = await _fixture.ResetAsync();
        var hierarchy = context.NestedSet<DerivedVersionBaseNode>().ForScope(1);
        await hierarchy.InsertRootAsync(new() { Id = 1, Name = "Root" }, Guid.Empty, CancellationToken.None);
        await hierarchy.InsertChildAsync(new DerivedVersionPlainNode { Id = 2, Name = "Zulu" }, 1,
            CancellationToken.None);
        var inserted = new DerivedVersionTokenNode { Id = 10, Name = "Alpha" };

        // Act
        if (bulk)
        {
            await hierarchy.InsertSubtreeAsync(new NestedSetBranch<DerivedVersionBaseNode>(inserted), 1,
                CancellationToken.None);
        }
        else
        {
            await hierarchy.InsertChildAsync(inserted, 1, CancellationToken.None);
        }

        var trackedCount = context.ChangeTracker.Entries().Count();
        var returned = inserted.Version.ToArray();
        var persisted = await context.Set<DerivedVersionTokenNode>().AsNoTracking()
            .SingleAsync(node => node.Id == inserted.Id, CancellationToken.None);
        context.Attach(inserted);
        inserted.Payload = "Attached payload";
        await context.SaveChangesAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, trackedCount);
        Assert.NotEmpty(returned);
        Assert.Equal(persisted.Version, returned);
        Assert.Equal((persisted.Left, persisted.Right, persisted.Depth, persisted.Position),
            (inserted.Left, inserted.Right, inserted.Depth, inserted.Position));
        var stored = await context.Set<DerivedVersionTokenNode>().AsNoTracking()
            .SingleAsync(node => node.Id == inserted.Id, CancellationToken.None);
        Assert.Equal("Attached payload", stored.Payload);
        Assert.Equal(stored.Version, inserted.Version);
        Assert.True((await hierarchy.InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>A failed bulk refresh restores mixed-subtype token inputs without retaining entries.</summary>
    [Fact]
    public async Task FailedBulkInsertionRestoresDerivedTokenInputs()
    {
        // Arrange
        var probe = new RefreshProbe { FailBulkRefresh = true };
        await using var context = await _fixture.ResetAsync(probe);
        var hierarchy = context.NestedSet<DerivedVersionBaseNode>().ForScope(1);
        await hierarchy.InsertRootAsync(new() { Id = 1, Name = "Root" }, Guid.Empty, CancellationToken.None);
        var input = new DerivedVersionTokenNode { Id = 10, Name = "Alpha", Version = [1, 2, 3] };
        var child = new DerivedVersionTokenNode { Id = 12, Name = "Child", Version = [4, 5, 6] };
        var inputToken = input.Version.ToArray();
        var childToken = child.Version.ToArray();
        var branch = new NestedSetBranch<DerivedVersionBaseNode>(input,
            [new(new DerivedVersionPlainNode { Id = 11, Name = "Plain" }), new(child)]);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchy.InsertSubtreeAsync(
            branch, 1, CancellationToken.None));

        // Assert
        Assert.True(probe.BulkRefreshAttempted);
        Assert.Equal("Derived token bulk refresh probe.", failure.Message);
        Assert.Equal(inputToken, input.Version);
        Assert.Equal(childToken, child.Version);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(1, await context.Set<DerivedVersionBaseNode>().CountAsync(CancellationToken.None));
        Assert.True((await hierarchy.InTree(Guid.Empty)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
    }

    /// <summary>A final refresh failure restores tokens already updated by the earlier parent-move refresh.</summary>
    [Fact]
    public async Task FailureAfterParentRefreshRestoresDerivedTokens()
    {
        // Arrange
        var probe = new RefreshProbe { FailAfterFirstRead = true };
        await using var context = await _fixture.ResetAsync(probe);
        var tracked = await SeedAsync(context);
        var changed = Assert.IsType<DerivedVersionTokenNode>(tracked.Single(node => node.Id == 2));
        var peer = tracked.Single(node => node.Id == 3);
        var tokens = tracked.OfType<DerivedVersionTokenNode>()
            .ToDictionary(node => node.Id, node => node.Version.ToArray());
        var before = await SnapshotAsync(context);
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
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.All(tracked.OfType<DerivedVersionTokenNode>(), node =>
        {
            Assert.Equal(tokens[node.Id], context.Entry(node).Property(value => value.Version).CurrentValue);
            Assert.Equal(tokens[node.Id], context.Entry(node).Property(value => value.Version).OriginalValue);
        });
        Assert.Equal(5, changed.ParentId);
        Assert.Equal("Zulu", changed.Name);
        Assert.Equal(EntityState.Modified, context.Entry(changed).State);
        Assert.Equal("Zulu peer", peer.Name);
        Assert.Equal(EntityState.Modified, context.Entry(peer).State);
        await AssertValidAsync(context);
    }

    /// <summary>Seeds a concrete base, an ordinary sibling, and three token-bearing descendants.</summary>
    private static async Task<DerivedVersionBaseNode[]> SeedAsync(
        DerivedRowVersionContext context
    )
    {
        var hierarchy = context.NestedSet<DerivedVersionBaseNode>().ForScope(1);
        await hierarchy.InsertForestAsync(
            [
                new NestedSetTreeImport<DerivedVersionBaseNode, Guid>(
                    Guid.Empty,
                    new NestedSetBranch<DerivedVersionBaseNode>(
                        new() { Id = 1, Name = "Root" },
                        [
                            new(new DerivedVersionTokenNode { Id = 2, Name = "Alpha" },
                                [new(new DerivedVersionTokenNode { Id = 4, Name = "Leaf" })]),
                            new(new DerivedVersionPlainNode { Id = 3, Name = "Middle" }),
                            new(new DerivedVersionTokenNode { Id = 5, Name = "Other" }),
                        ])),
                new NestedSetTreeImport<DerivedVersionBaseNode, Guid>(
                    s_destinationTree,
                    new NestedSetBranch<DerivedVersionBaseNode>(new() { Id = 6, Name = "Destination root" })),
            ],
            CancellationToken.None);

        return await context.Set<DerivedVersionBaseNode>().OrderBy(node => node.Id)
            .ToArrayAsync(CancellationToken.None);
    }

    /// <summary>Observes payload, tokens, and every structural value without consulting the tracker.</summary>
    private static async Task<string[]> SnapshotAsync(
        DerivedRowVersionContext context
    )
    {
        var rows = await context.Set<DerivedVersionBaseNode>().AsNoTracking()
            .OrderBy(node => node.Id).ToArrayAsync(CancellationToken.None);

        return rows.Select(node =>
                $"{node.Id}/{node.ParentId}/{node.TreeId}/{node.Left}/{node.Right}/{node.Depth}/{node.Position}"
                + $"/{node.Name}/{node.Payload}/"
                + (node is DerivedVersionTokenNode versioned ? Convert.ToHexString(versioned.Version) : string.Empty))
            .ToArray();
    }

    /// <summary>Verifies persisted adjacency, bounds, positions, and ordering after the token probe.</summary>
    private static async Task AssertValidAsync(
        DerivedRowVersionContext context
    )
    {
        foreach (var tree in new[] { Guid.Empty, s_destinationTree })
        {
            Assert.True((await context.NestedSet<DerivedVersionBaseNode>().ForScope(1).InTree(tree)
                .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None)).IsValid);
        }
    }

    /// <summary>Observes only token refresh reads after payload SQL, with an optional late failure.</summary>
    private sealed class RefreshProbe : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        /// <summary>Gets or sets whether the enclosing test has completed its payload save.</summary>
        internal bool Active { get; set; }

        /// <summary>Gets or sets whether a second token refresh should fail after the first one completed.</summary>
        internal bool FailAfterFirstRead { get; init; }

        /// <summary>Gets or sets whether the atomic import's tagged final refresh should fail.</summary>
        internal bool FailBulkRefresh { get; init; }

        /// <summary>Gets whether the import reached its final refresh after assigning generated input values.</summary>
        internal bool BulkRefreshAttempted { get; private set; }

        /// <summary>Gets scalar refresh commands without recording seed or application payload reads.</summary>
        internal System.Collections.Generic.List<string> Commands { get; } = [];

        /// <inheritdoc />
        public override ValueTask<
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>>
            ReaderExecutingAsync(
                System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
                CancellationToken cancellationToken = default
            )
        {
            if (FailBulkRefresh && command.CommandText.Contains(
                    "Doka.EntityFrameworkCore.NestedSet.BulkRefresh", StringComparison.Ordinal))
            {
                BulkRefreshAttempted = true;

                throw new InvalidOperationException("Derived token bulk refresh probe.");
            }

            if (Active && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("[Version]", StringComparison.Ordinal))
            {
                Commands.Add(command.CommandText);

                if (FailAfterFirstRead && Commands.Count == 2)
                {
                    throw new InvalidOperationException("Derived token late refresh probe.");
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
