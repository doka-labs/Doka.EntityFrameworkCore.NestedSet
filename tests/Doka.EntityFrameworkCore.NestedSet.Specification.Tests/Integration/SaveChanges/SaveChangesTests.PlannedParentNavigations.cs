using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Accepts explicitly marked parents alongside the application's own navigation synchronization.</summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task MarkedParentWithSynchronizedNavigationsCommits(string navigation, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true);
        var graph = await PreparePlannedParentAsync(context, navigation, automaticDetection, false, false);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedParent = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        var validation = await verification.NestedSet<NavigationNode>().ForScope(graph.Child.Scope).InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal(3, storedParent);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Callback failure restores current, original, navigation and relationship state exactly.</summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task FailedMarkedParentMoveRestoresNavigationSnapshots(string navigation, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, failure);
        var graph = await PreparePlannedParentAsync(context, navigation, automaticDetection, true, true);
        var before = CapturePlannedParentState(context);

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedParent = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        var storedNote = await verification.Set<NavigationNote>().Select(note => note.Value)
            .SingleAsync(CancellationToken.None);

        var validation = await verification.NestedSet<NavigationNode>().ForScope(graph.Child.Scope).InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, error);
        AssertPlannedParentState(before, after);
        Assert.Equal(1, storedParent);
        Assert.Equal("original", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>
    ///     The identical retry succeeds without repairing any graph, scalar, flag or relationship snapshot.
    /// </summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task MarkedParentMoveRetriesIdenticallyAfterCallbackFailure(string navigation, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, failure);
        var graph = await PreparePlannedParentAsync(context, navigation, automaticDetection, true, true);
        var before = CapturePlannedParentState(context);
        // WHY: The failed boundary is this retry test's precondition; its observer fails only once.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var afterFailure = CapturePlannedParentState(context);

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedParent = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        var storedNote = await verification.Set<NavigationNote>().Select(note => note.Value)
            .SingleAsync(CancellationToken.None);

        var validation = await verification.NestedSet<NavigationNode>().ForScope(graph.Child.Scope).InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Same(failure.Error, firstError);
        AssertPlannedParentState(before, afterFailure);
        Assert.Equal(3, storedParent);
        Assert.Equal("planned payload", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A committed marked parent move leaves the same context usable for ordinary application work.</summary>
    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("collections", false)]
    [InlineData("collections", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task MarkedParentMoveAllowsFollowingOrdinarySave(string navigation, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true);
        var graph = await PreparePlannedParentAsync(context, navigation, automaticDetection, false, false);
        await context.SaveChangesAsync(CancellationToken.None);
        // WHY: Reuse the captured property entry without an incidental EntityEntry-local detection pass.
        graph.NoteValue.CurrentValue = "following payload";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedParent = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        var storedNote = await verification.Set<NavigationNote>().Select(note => note.Value)
            .SingleAsync(CancellationToken.None);

        var validation = await verification.NestedSet<NavigationNode>().ForScope(graph.Child.Scope).InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal(3, storedParent);
        Assert.Equal("following payload", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A planned parent never authorizes callback navigation edits toward a different real parent.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PlannedParentRejectsDifferentCallbackNavigation(bool collection, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();

        await using (var setup = await CreateNavigationContextAsync(database, wrapped: true))
        {
            var tree = setup.NestedSet<NavigationNode>().ForScope(1);
            await tree.InsertSubtreeAsync(
                new NestedSetBranch<NavigationNode>(new NavigationNode { Id = 4, Name = "Charlie" },
                [
                    new NestedSetBranch<NavigationNode>(new NavigationNode { Id = 5, Name = "Delta" }),
                    new NestedSetBranch<NavigationNode>(new NavigationNode { Id = 6, Name = "Echo" }),
                ]), 1, CancellationToken.None);

            await tree.InsertChildAsync(new NavigationNode { Id = 7, Name = "Foxtrot" }, 1, CancellationToken.None);
        }

        var callback = new SaveBoundaryCallback();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback, probe);
        var graph = await PreparePlannedParentAsync(context, "both", automaticDetection, true, true);
        var nodes = context.GetService<IUpdateAdapterFactory>().Create().Entries
            .Select(entry => entry.ToEntityEntry().Entity).OfType<NavigationNode>().ToArray();

        var other = nodes.Single(node => node.Id == 4);
        var secondOther = nodes.Single(node => node.Id == 7);
        var before = CapturePlannedParentState(context);
        callback.OnSaving = _ =>
        {
            if (collection)
            {
                // WHY: Cleanup must visit both unknown targets and preserve Charlie's existing child order.
                other.Children.Add(graph.Child);
                secondOther.Children.Add(graph.Child);
            }
            else
            {
                graph.Child.Parent = other;
            }
        };

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedParent = await verification.Set<NavigationNode>().Where(node => node.Id == 2)
            .Select(node => node.ParentId).SingleAsync(CancellationToken.None);

        var storedNote = await verification.Set<NavigationNote>().Select(note => note.Value)
            .SingleAsync(CancellationToken.None);

        var validation = await verification.NestedSet<NavigationNode>().ForScope(graph.Child.Scope).InTree(s_treeId)
            .ValidateAsync(NestedSetValidationLevel.Full, CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, probe.Writes);
        AssertPlannedParentState(before, after);
        Assert.Equal(1, storedParent);
        Assert.Equal("original", storedNote);
        Assert.Empty(validation.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Known facade inputs retained by a principal collection do not block later ordinary saves.</summary>
    [Theory]
    [InlineData("single", false)]
    [InlineData("single", true)]
    [InlineData("subtree", false)]
    [InlineData("subtree", true)]
    [InlineData("forest", false)]
    [InlineData("forest", true)]
    public async Task PreparedOwnerInputAllowsFollowingOrdinarySave(string shape, bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedPreparedNavigationDestinationAsync(database);
        var callback = new SaveBoundaryCallback();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, callback);
        var owner = await context.Set<NavigationScopeOwner>()
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var noteValue = context.Entry(note).Property(value => value.Value);
        var inputs = PreparedNavigationInputs(shape, owner.Id);
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (!automaticDetection)
        {
            owner.Nodes.Add(inputs[0]);
        }

        IUpdateEntry? inputEntry = null;
        callback.OnSaving = current =>
        {
            if (automaticDetection && !owner.Nodes.Contains(inputs[0]))
            {
                // WHY: Automatic mode's existing preflight must not attach a new graph before authorization.
                owner.Nodes.Add(inputs[0]);
            }

            inputEntry = current.GetService<IUpdateAdapterFactory>().Create().Entries
                .Single(entry => ReferenceEquals(entry.ToEntityEntry().Entity, inputs[0]));
        };

        await InsertPreparedNavigationShapeAsync(context, shape, inputs);
        var stateAfterInsertion = inputEntry?.EntityState;
        callback.OnSaving = null;
        noteValue.CurrentValue = "following payload";

        // Act
        await context.SaveChangesAsync(CancellationToken.None);
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var storedNote = await verification.Set<NavigationNote>().Select(value => value.Value)
            .SingleAsync(CancellationToken.None);

        var count = await verification.Set<NavigationNode>().CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(EntityState.Detached, stateAfterInsertion);
        Assert.Contains(inputs[0], owner.Nodes);
        Assert.Equal("following payload", storedNote);
        Assert.Equal(4 + inputs.Length, count);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Automatic detection still rejects a prepared input registered before facade authorization.</summary>
    [Theory]
    [InlineData("single")]
    [InlineData("subtree")]
    [InlineData("forest")]
    public async Task AutomaticPreparedOwnerInputPreservesPendingWritePreflight(string shape)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        await SeedPreparedNavigationDestinationAsync(database);
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, probe);
        var owner = await context.Set<NavigationScopeOwner>()
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        var inputs = PreparedNavigationInputs(shape, owner.Id);
        owner.Nodes.Add(inputs[0]);

        // Act
        var failure = await Record.ExceptionAsync(() => InsertPreparedNavigationShapeAsync(context, shape, inputs));
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var count = await verification.Set<NavigationNode>().CountAsync(CancellationToken.None);

        // Assert
        var rejection = Assert.IsType<NestedSetException>(failure);
        Assert.Equal(NestedSetErrorCode.InvalidContext, rejection.Code);
        Assert.Contains("no pending tracked changes", rejection.Message, StringComparison.Ordinal);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(4, count);
    }

    /// <summary>The persisted-input exception never authorizes an unrelated untracked collection member.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArbitraryUntrackedOwnerMemberStillRejectsFollowingSave(bool automaticDetection)
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync();
        var probe = new NavigationWriteProbe();
        await using var context = await CreateNavigationContextAsync(database, wrapped: true, probe);
        var owner = await context.Set<NavigationScopeOwner>()
            .SingleAsync(value => value.Value == "owner", CancellationToken.None);

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;
        owner.Nodes.Add(new NavigationNode { Id = 999, Scope = owner.Id, Name = "Unrequested" });

        // Act
        var failure = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        await using var verification = await CreateNavigationContextAsync(database, wrapped: true);
        var count = await verification.Set<NavigationNode>().CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NestedSetErrorCode.InvalidContext, Assert.IsType<NestedSetException>(failure).Code);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(3, count);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }
}
