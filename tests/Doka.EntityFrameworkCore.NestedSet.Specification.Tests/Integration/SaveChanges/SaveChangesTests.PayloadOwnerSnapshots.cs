using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Only an actually persisted ordinary FK can authorize a prepared owner's detached member.</summary>
    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, true)]
    public async Task PayloadOwnerSnapshotRequiresPersistedForeignKey(
        bool marked,
        bool coordinated,
        bool wrapped,
        bool synchronous
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedPayloadOwnersAsync(
            database,
            assigned: true,
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

        var target = await context
            .Set<NavigationPayloadOwner>()
            .Include(value => value.Nodes)
            .SingleAsync(value => value.Scope == node.Scope && value.Id == 2, CancellationToken.None);

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

        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;

        if (coordinated)
        {
            node.Name = "Zulu";
            name.IsModified = true;
        }

        node.PayloadOwnerId = target.Id;

        if (marked)
        {
            // WHY: Mark the raw setter through its retained property; CurrentValue would perform FK fixup itself.
            payloadOwner.IsModified = true;
        }

        target.Nodes.Add(node);
        var snapshotBefore = PayloadOwnerSnapshotContains(context, target, node);

        // WHY: This first save is the persistence precondition for testing a later detached collection member.
        await SaveNavigationAsync(context, synchronous, cancellationToken: TestContext.Current.CancellationToken);
        var snapshotAfter = PayloadOwnerSnapshotContains(context, target, node);
        await using var verification = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var storedAfterSave = await verification
            .Set<NavigationNode>()
            .Where(value => value.Id == node.Id)
            .Select(value => new
            {
                value.Name,
                value.PayloadOwnerId,
            })
            .SingleAsync(CancellationToken.None);

        nodeEntry.State = EntityState.Detached;
        noteValue.CurrentValue = "following payload";
        var writesBeforeFollowup = probe.Writes;

        // Act
        var error = await Record.ExceptionAsync(() => SaveNavigationAsync(
            context,
            synchronous,
            cancellationToken: TestContext.Current.CancellationToken));

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
        Assert.False(snapshotBefore);
        Assert.Equal(marked, snapshotAfter);
        Assert.Equal(
            coordinated
                ? "Zulu"
                : "Alpha",
            storedAfterSave.Name);

        Assert.Equal(
            marked
                ? 2
                : 1,
            storedAfterSave.PayloadOwnerId);

        if (marked)
        {
            Assert.Null(error);
            Assert.Equal("following payload", storedNote);
        }
        else
        {
            Assert.Equal(
                NestedSetErrorCode.InvalidContext,
                Assert.IsType<NestedSetException>(error)
                    .Code);

            Assert.Equal(writesBeforeFollowup, probe.Writes);
            Assert.Equal("original", storedNote);
        }

        Assert.Equal(storedAfterSave.PayloadOwnerId, storedOwner);
        Assert.Contains(node, target.Nodes);
        Assert.Empty(validation.Issues);
        Assert.Equal(0, detections);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A completion callback cannot turn an unsaved ordinary FK into a proven saved collection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadOwnerSnapshotExcludesPostPersistenceCallbackMutation(
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

        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
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
        EventHandler<SavedChangesEventArgs> completion = (_, _) =>
        {
            // WHY: This raw FK is assigned after SQL; a late current-value read cannot prove it was persisted.
            node.PayloadOwnerId = callbackTarget.Id;
            callbackTarget.Nodes.Add(node);
        };

        context.SavedChanges += completion;
        await context.SaveChangesAsync(CancellationToken.None);
        context.SavedChanges -= completion;
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

    /// <summary>A command-completion mutation cannot replace the FK values sent by the original SQL update.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadOwnerSnapshotExcludesCommandCompletionMutation(
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

    /// <summary>Reads installed relationship membership without triggering entity or tracker detection.</summary>
    private static bool PayloadOwnerSnapshotContains(
        DbContext context,
        NavigationPayloadOwner owner,
        NavigationNode node
    )
    {
        var entry = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(value => ReferenceEquals(
                value.ToEntityEntry()
                    .Entity,
                owner));

        var navigation = entry.EntityType.FindNavigation(nameof(NavigationPayloadOwner.Nodes))!;

        return entry.GetRelationshipSnapshotValue(navigation) is IEnumerable<object> members
            && members.Any(member => ReferenceEquals(member, node));
    }

    /// <summary>Mutates ordinary payload only after the first actual hierarchy UPDATE command returns.</summary>
    private sealed class PayloadOwnerCommandCallback : DbCommandInterceptor
    {
        /// <summary>Gets or sets the one mutation performed after the UPDATE was sent to the database.</summary>
        internal Action? OnUpdate { get; set; }

        /// <summary>Gets whether an actual payload UPDATE reached the provider-completion boundary.</summary>
        internal bool Reached { get; private set; }

        /// <inheritdoc />
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command);

            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command);

            return ValueTask.FromResult(result);
        }

        /// <summary>Ignores reads and later ordering commands so only the first sent-value boundary changes.</summary>
        private void Observe(
            DbCommand command
        )
        {
            var sql = command.CommandText;

            // WHY: Match the table immediately after UPDATE so a provider's SELECT FOR UPDATE cannot fire this hook.
            if (Reached
                || !sql.Contains(nameof(NavigationNode.PayloadOwnerId), StringComparison.Ordinal)
                || !(sql.Contains("UPDATE \"NavigationSaveNodes\"", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("UPDATE [NavigationSaveNodes]", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("UPDATE `NavigationSaveNodes`", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Reached = true;
            OnUpdate?.Invoke();
        }
    }
}
