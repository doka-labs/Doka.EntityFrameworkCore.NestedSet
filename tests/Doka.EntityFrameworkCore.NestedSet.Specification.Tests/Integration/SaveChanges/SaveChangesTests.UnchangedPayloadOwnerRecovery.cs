namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>A callback cannot hide a changed installed FK behind an unchanged captured scalar value.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UnchangedPayloadOwnerRollbackRestoresHiddenInstalledRelationship(
        bool marked,
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedUnchangedPayloadOwnerAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var initiallyModified = graph.ForeignKey.IsModified;
        var before = CapturePlannedParentState(context);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageUnchangedPayloadOwnerCallback(graph, marked);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(initiallyModified);
        AssertPlannedParentState(before, after);
        Assert.Same(expectedError, error);
        Assert.Equal(2, graph.Node.PayloadOwnerId);
        Assert.Equal(2, graph.ForeignKey.OriginalValue);
        Assert.False(graph.ForeignKey.IsModified);
        Assert.Same(graph.Target, graph.Node.PayloadOwner);
        Assert.Contains(graph.Node, graph.Target.Nodes);
        Assert.DoesNotContain(graph.Node, graph.Previous.Nodes);
        Assert.Contains(graph.Node, PayloadOwnerNativeMembers(context, graph.Target));
        Assert.DoesNotContain(graph.Node, PayloadOwnerNativeMembers(context, graph.Previous));
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A later retry must not need automatic detection to repair the rolled-back native bucket.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task UnchangedPayloadOwnerRollbackAllowsIndependentRetry(
        bool marked,
        bool hierarchyFailure,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedUnchangedPayloadOwnerAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);
        var detections = 0;
        context.ChangeTracker.DetectedAllChanges += (_, _) => detections++;
        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageUnchangedPayloadOwnerCallback(graph, marked);
        };

        // WHY: The first rollback is the retry precondition; no relationship value, flag or collection is repaired.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var afterFailure = CapturePlannedParentState(context);
        var failedDetections = detections;
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        // Act
        var retryError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var stored = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignments = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertPlannedParentState(before, afterFailure);
        Assert.Same(expectedError, firstError);
        Assert.Equal(0, failedDetections);
        Assert.Null(retryError);
        Assert.Equal(
            2,
            assignments.Single(assignment => assignment.Id == graph.Node.Id)
                .PayloadOwnerId);

        Assert.Equal(
            "Zulu",
            stored.Nodes.Single(node => node.Id == graph.Node.Id)
                .Name);

        Assert.Same(graph.Target, graph.Node.PayloadOwner);
        Assert.Contains(graph.Node, graph.Target.Nodes);
        Assert.DoesNotContain(graph.Node, graph.Previous.Nodes);
        Assert.Contains(graph.Node, PayloadOwnerNativeMembers(context, graph.Target));
        Assert.DoesNotContain(graph.Node, PayloadOwnerNativeMembers(context, graph.Previous));
        Assert.False(graph.ForeignKey.IsModified);

        if (automaticDetection)
        {
            Assert.True(detections > 0);
        }
        else
        {
            Assert.Equal(0, detections);
        }

        Assert.Equal(0, stored.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>A restored original member remains usable after detachment under either detection policy.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task UnchangedPayloadOwnerRollbackAllowsDetachedOrdinaryFollowup(
        bool marked,
        bool hierarchyFailure,
        bool automaticDetection
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedUnchangedPayloadOwnerAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        var before = CapturePlannedParentState(context);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageUnchangedPayloadOwnerCallback(graph, marked);
        };

        // WHY: Capture rollback evidence before detachment or later native detection could hide the original error.
        var firstError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var afterFailure = CapturePlannedParentState(context);
        graph.Entry.State = EntityState.Detached;
        graph.Note.CurrentValue = "following payload";
        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        // Act
        var followupError = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        AssertPlannedParentState(before, afterFailure);
        Assert.Same(expectedError, firstError);
        Assert.Null(followupError);
        Assert.Equal(EntityState.Detached, graph.Entry.State);
        Assert.Contains(graph.Node, graph.Target.Nodes);
        Assert.DoesNotContain(graph.Node, graph.Previous.Nodes);
        Assert.Contains(graph.Node, PayloadOwnerNativeMembers(context, graph.Target));
        Assert.DoesNotContain(graph.Node, PayloadOwnerNativeMembers(context, graph.Previous));
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal("following payload", storedAfter.Note);
        Assert.Equal(0, storedAfter.Issues);
        Assert.Equal(automaticDetection, context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Rollback preserves list order, raw reference divergence and opposite native identities.</summary>
    [Theory]
    [InlineData("middle", false, false)]
    [InlineData("middle", false, true)]
    [InlineData("middle", true, false)]
    [InlineData("middle", true, true)]
    [InlineData("null-reference", false, false)]
    [InlineData("null-reference", false, true)]
    [InlineData("null-reference", true, false)]
    [InlineData("null-reference", true, true)]
    [InlineData("opposite", false, false)]
    [InlineData("opposite", false, true)]
    [InlineData("opposite", true, false)]
    [InlineData("opposite", true, true)]
    public async Task UnchangedPayloadOwnerRollbackPreservesOriginalGraphShape(
        string shape,
        bool marked,
        bool hierarchyFailure
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedUnchangedPayloadOwnerAsync(database, cancellationToken: TestContext.Current.CancellationToken);

        await using (var setup = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            switch (shape)
            {
                case "middle":
                    await setup
                        .Set<NavigationNode>()
                        .Where(node => node.Id >= 1 && node.Id <= 3)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(node => node.PayloadOwnerId, 2),
                            CancellationToken.None);

                    break;
                case "opposite":
                    await setup
                        .Set<NavigationNode>()
                        .Where(node => node.Id == 3)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(node => node.PayloadOwnerId, 1),
                            CancellationToken.None);

                    break;
                case "null-reference":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        var callback = new SaveBoundaryCallback();
        var (failure, expectedError) = CreatePayloadOwnerRecoveryFailure(hierarchyFailure);
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        ArmPayloadOwnerHierarchyFailure(context, failure);
        var graph = await PreparePayloadOwnerRecoveryAsync(
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        EntityEntry<NavigationNode>? opposite = null;
        PropertyEntry<NavigationNode, int?>? oppositeForeignKey = null;

        switch (shape)
        {
            case "middle":
                // WHY: These are actual stored members; sorting only CLR order places A in the exact middle.
                graph.Target.Nodes.Sort((left, right) => left.Id.CompareTo(right.Id));
                break;
            case "null-reference":
                // WHY: A raw ordinary reference is independent of its correctly installed FK under manual detection.
                graph.Node.PayloadOwner = null;
                break;
            case "opposite":
                opposite = context.Entry(graph.Previous.Nodes.Single(node => node.Id == 3));
                oppositeForeignKey = opposite.Property(node => node.PayloadOwnerId);
                break;
        }

        var originalTargetIds = graph
            .Target
            .Nodes
            .Select(node => node.Id)
            .ToArray();

        var originalReference = graph.Node.PayloadOwner;
        var before = CapturePlannedParentState(context);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;

            if (opposite is not null
                && oppositeForeignKey is not null)
            {
                // WHY: Both captured scalars return to their originals while their native buckets move oppositely.
                oppositeForeignKey.CurrentValue = graph.Target.Id;
                opposite.Entity.PayloadOwnerId = graph.Previous.Id;
                oppositeForeignKey.IsModified = marked;

                if (marked)
                {
                    graph.Previous.Nodes.Add(opposite.Entity);
                }
            }

            StageUnchangedPayloadOwnerCallback(graph, marked);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        if (shape == "middle")
        {
            Assert.Equal([1, 2, 3], originalTargetIds);
            Assert.Equal(graph.Node.Id, originalTargetIds[1]);
        }
        else if (shape == "null-reference")
        {
            Assert.Null(originalReference);
        }

        AssertPlannedParentState(before, after);
        Assert.Same(expectedError, error);
        Assert.Equal(originalTargetIds, graph.Target.Nodes.Select(node => node.Id));
        Assert.Same(originalReference, graph.Node.PayloadOwner);
        Assert.False(graph.ForeignKey.IsModified);

        if (oppositeForeignKey is not null)
        {
            Assert.False(oppositeForeignKey.IsModified);
        }

        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Rollback preserves native absence even when the installed FK already names that owner.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedPayloadOwnerRollbackPreservesOriginallyAbsentNativeMember(
        bool marked
    )
    {
        // Arrange
        await using var database = await CreateNavigationDatabaseAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        await SeedUnchangedPayloadOwnerAsync(database, cancellationToken: TestContext.Current.CancellationToken);
        var callback = new SaveBoundaryCallback();
        var failure = new PlannedParentFailure();
        await using var context = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: TestContext.Current.CancellationToken,
            callback,
            failure);

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var node = await context
            .Set<NavigationNode>()
            .AsNoTracking()
            .SingleAsync(value => value.Id == 2, CancellationToken.None);

        var owners = await context
            .Set<NavigationPayloadOwner>()
            .Where(owner => owner.Scope == node.Scope)
            .ToArrayAsync(CancellationToken.None);

        var previous = owners.Single(owner => owner.Id == 1);
        var target = owners.Single(owner => owner.Id == 2);
        var note = await context
            .Set<NavigationNote>()
            .SingleAsync(CancellationToken.None);

        // WHY: Public Attach sees a preprepared CLR member, so EF can skip adding that member to its native set.
        target.Nodes.Add(node);
        var entry = context.Attach(node);
        var name = entry.Property(value => value.Name);
        var graph = new PayloadOwnerRecoveryGraph(
            node,
            entry,
            previous,
            target,
            entry.Property(value => value.PayloadOwnerId),
            context
                .Entry(note)
                .Property(value => value.Value));

        node.Name = "Zulu";
        name.IsModified = true;
        var nativeBefore = PayloadOwnerNativeMembers(context, target);
        var before = CapturePlannedParentState(context);
        var storedBefore = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsBefore = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        callback.OnSaving = _ =>
        {
            callback.OnSaving = null;
            StageUnchangedPayloadOwnerCallback(graph, marked);
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        var after = CapturePlannedParentState(context);
        var nativeAfter = PayloadOwnerNativeMembers(context, target);
        var storedAfter = await ReadPreparedOwnerStoreAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        var assignmentsAfter = await ReadMixedPayloadOwnerAssignmentsAsync(
            database,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(node, nativeBefore);
        AssertPlannedParentState(before, after);
        Assert.Same(failure.Error, error);
        Assert.DoesNotContain(node, nativeAfter);
        Assert.Contains(node, target.Nodes);
        Assert.DoesNotContain(node, previous.Nodes);
        Assert.Equal(storedBefore.Nodes, storedAfter.Nodes);
        Assert.Equal(storedBefore.Note, storedAfter.Note);
        Assert.Equal(assignmentsBefore, assignmentsAfter);
        Assert.Equal(0, storedAfter.Issues);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Persists the ordinary target relationship before the tested context loads any entries.</summary>
    private static async Task SeedUnchangedPayloadOwnerAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await SeedPayloadOwnersAsync(database, assigned: true, cancellationToken: cancellationToken);
        await using var setup = await CreateNavigationContextAsync(
            database,
            wrapped: true,
            cancellationToken: cancellationToken);

        await setup
            .Set<NavigationNode>()
            .Where(node => node.Id == 2)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(node => node.PayloadOwnerId, 2),
                cancellationToken);
    }

    /// <summary>Uses genuine fixup before a raw reset that does not notify EF about its installed identity.</summary>
    private static void StageUnchangedPayloadOwnerCallback(
        PayloadOwnerRecoveryGraph graph,
        bool marked
    )
    {
        graph.ForeignKey.CurrentValue = graph.Previous.Id;
        graph.Node.PayloadOwnerId = graph.Target.Id;
        graph.ForeignKey.IsModified = marked;

        if (marked)
        {
            // WHY: The unmarked control performs no library collection addition and still requires native repair.
            graph.Target.Nodes.Add(graph.Node);
        }
    }
}
