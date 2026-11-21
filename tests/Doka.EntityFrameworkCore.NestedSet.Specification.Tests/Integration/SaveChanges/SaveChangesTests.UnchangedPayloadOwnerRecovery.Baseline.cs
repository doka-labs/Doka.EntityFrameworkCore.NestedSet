namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
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
    public async Task SaveChangesAsync_UnchangedPayloadOwnerRollback_PreservesOriginalGraphShape(
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
    public async Task SaveChangesAsync_UnchangedPayloadOwnerRollback_PreservesOriginallyAbsentNativeMember(
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
}
