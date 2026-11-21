using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;
using ProbeNode = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeNode;
using ProbeOwner = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeOwner;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks complete prepared-owner authorization while native tracking advances in bounded waves.</summary>
public sealed class HierarchyImportCollectionScaleTests
{
    private const int BatchSize = 64;

    /// <summary>Every validated input remains authorized while completed native waves detach.</summary>
    /// <param name="nodes">The complete prepared collection size, including detached future inputs.</param>
    [Theory]
    [InlineData(1_000)]
    [InlineData(2_000)]
    [InlineData(4_000)]
    [InlineData(8_000)]
    public async Task TrackWave_PreparedImportGuard_PreservesBoundedNativeWaves(
        int nodes
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var prepared = Prepare(context, nodes);
        using var managed = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        using var import = NestedSetSaveChanges.EnterImportInputs(context, prepared.Inputs);
        TrackWave(context, prepared.Nodes, 0, BatchSize);
        var changes = 0;
        var maximumTracked = 0;

        // Act
        // WHY: Every completed prefix remains in the native baseline while only one bounded wave stays tracked.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var offset = 0; offset < nodes; offset += BatchSize)
        {
            var count = Math.Min(BatchSize, nodes - offset);

            if (offset != 0)
            {
                TrackWave(context, prepared.Nodes, offset, count);
            }

            maximumTracked = Math.Max(
                maximumTracked,
                context
                    .ChangeTracker
                    .Entries()
                    .Count());

            var observation = Observe(context);
            changes += observation;
            CompleteWave(context, prepared, offset, count);
        }

        // Assert
        var snapshot = Assert.IsAssignableFrom<ISet<object>>(
            prepared.OwnerEntry.GetRelationshipSnapshotValue(prepared.Navigation));

        Assert.Equal(0, changes);
        Assert.Equal(BatchSize + 1, maximumTracked);
        Assert.Equal(nodes, snapshot.Count);
        Assert.Equal(nodes, prepared.Owner.ScopeNodes.Count);
        Assert.Single(context.ChangeTracker.Entries());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Equal native reference sets remain valid with both small and large detached prefixes.</summary>
    [Fact]
    public async Task Observe_CompletedSnapshot_PreservesDetachedPrefixAndTrackedWave()
    {
        // Arrange
        await using var smallContext = new SnapshotContext();
        await using var largeContext = new SnapshotContext();
        var small = Prepare(smallContext, 1_000);
        var large = Prepare(largeContext, 8_000);
        using var smallManaged = NestedSetSaveChanges.EnterManagedSave(smallContext, [], static () => { });
        using var smallImport = NestedSetSaveChanges.EnterImportInputs(smallContext, small.Inputs);
        using var largeManaged = NestedSetSaveChanges.EnterManagedSave(largeContext, [], static () => { });
        using var largeImport = NestedSetSaveChanges.EnterImportInputs(largeContext, large.Inputs);
        InstallCompletedSnapshot(smallContext, small);
        InstallCompletedSnapshot(largeContext, large);

        // Act
        var smallObservation = Observe(smallContext);
        var largeObservation = Observe(largeContext);

        // Assert
        Assert.Equal(0, smallObservation);
        Assert.Equal(0, largeObservation);
        Assert.Equal(
            BatchSize + 1,
            smallContext
                .ChangeTracker
                .Entries()
                .Count());

        Assert.Equal(
            BatchSize + 1,
            largeContext
                .ChangeTracker
                .Entries()
                .Count());

        Assert.False(smallContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.False(largeContext.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            smallContext.Database.GetDbConnection()
                .State);

        Assert.Equal(
            ConnectionState.Closed,
            largeContext.Database.GetDbConnection()
                .State);
    }

    /// <summary>Whole-import authorization does not admit a foreign reference between callback boundaries.</summary>
    /// <param name="replace">Whether the foreign reference replaces an input without changing collection count.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HasHierarchyChanges_PreparedImport_RejectsForeignCollectionReference(
        bool replace
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var prepared = Prepare(context, 130);
        using var managed = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        using var import = NestedSetSaveChanges.EnterImportInputs(context, prepared.Inputs);
        var initial = NestedSetSaveChanges.HasHierarchyChanges(context);
        var foreign = HierarchyChangeDetectionTests.CreateNode(1_000);
        var members = Assert.IsAssignableFrom<List<ProbeNode>>(prepared.Owner.ScopeNodes);

        if (replace)
        {
            members[^1] = foreign;
        }
        else
        {
            members.Add(foreign);
        }

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(initial);
        Assert.True(changed);
        Assert.Equal(
            replace
                ? 130
                : 131,
            members.Count);

        Assert.Equal(
            EntityState.Detached,
            context.Entry(foreign)
                .State);

        Assert.Single(context.ChangeTracker.Entries());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Duplicate prepared references retain set semantics without tracking future inputs.</summary>
    [Fact]
    public async Task HasHierarchyChanges_PreparedImport_PreservesDuplicateReferenceSemantics()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var prepared = Prepare(context, 130);
        using var managed = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        using var import = NestedSetSaveChanges.EnterImportInputs(context, prepared.Inputs);
        prepared.Owner.ScopeNodes.Add(prepared.Nodes[^1]);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.False(changed);
        Assert.Equal(131, prepared.Owner.ScopeNodes.Count);
        Assert.Equal(2, prepared.Owner.ScopeNodes.Count(value => ReferenceEquals(value, prepared.Nodes[^1])));
        Assert.Single(context.ChangeTracker.Entries());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Removing a still-tracked required scope relationship remains guarded during an import.</summary>
    [Fact]
    public async Task HasHierarchyChanges_PreparedImport_RejectsTrackedSnapshotMemberRemoval()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var prepared = Prepare(context, 130);
        using var managed = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        using var import = NestedSetSaveChanges.EnterImportInputs(context, prepared.Inputs);
        TrackWave(context, prepared.Nodes, 0, 1);
        NestedSetInsertionContract.Instance.AddCollectionSnapshot(
            prepared.OwnerEntry,
            prepared.Navigation,
            prepared.Nodes[0]);

        var entry = context.Entry(prepared.Nodes[0]);
        var removed = prepared.Owner.ScopeNodes.Remove(prepared.Nodes[0]);

        // Act
        var changed = NestedSetSaveChanges.HasHierarchyChanges(context);

        // Assert
        Assert.True(removed);
        Assert.True(changed);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(prepared.Owner.Id, prepared.Nodes[0].Scope);
        Assert.Same(prepared.Owner, prepared.Nodes[0].ScopeOwner);
        Assert.False(
            entry.Property(value => value.Scope)
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    /// <summary>Creates actual validated import membership while leaving every input untracked.</summary>
    private static PreparedImport Prepare(
        SnapshotContext context,
        int count
    )
    {
        var owner = new ProbeOwner { Id = 7 };
        var ownerEntry = context.Entry(owner);
        ownerEntry.State = EntityState.Unchanged;
        var nodes = Enumerable
            .Range(1, count)
            .Select(HierarchyChangeDetectionTests.CreateNode)
            .ToArray();

        var metadata = context.Model.FindEntityType(typeof(ProbeNode))!;
        var store = new NestedSetStore<ProbeNode, int, Guid, int>(context, metadata, owner.Id, nodes[0].TreeId);
        var root = new NestedSetBranch<ProbeNode>(
            nodes[0],
            nodes
                .Skip(1)
                .Select(node => new NestedSetBranch<ProbeNode>(node))
                .ToArray());

        var plan = new NestedSetBulkPlan<ProbeNode, int, Guid, int>(
            store,
            [root],
            new HashSet<ProbeNode>(ReferenceEqualityComparer.Instance),
            CancellationToken.None);

        var destination = new NestedSetPlacementResolver<ProbeNode, int, Guid, int>.Destination(1, default, 0, 0);
        plan.RequireCapacity(destination);
        plan.PrepareGeometry(destination, null, CancellationToken.None);
        var inputs = NestedSetBulkPlan<ProbeNode, int, Guid, int>.CreateImportInputs([plan]);

        foreach (var node in nodes)
        {
            owner.ScopeNodes.Add(node);
        }

        var navigation = metadata.FindNavigation(nameof(ProbeNode.ScopeOwner))!.Inverse!;

        return new PreparedImport(
            owner,
            NestedSetInsertionContract.Instance.Entry(ownerEntry),
            nodes,
            navigation,
            inputs);
    }

    /// <summary>Tracks only one native wave without graph traversal into the complete prepared collection.</summary>
    private static void TrackWave(
        SnapshotContext context,
        ProbeNode[] nodes,
        int offset,
        int count
    )
    {
        // WHY: Entry.State tracks this exact node; Attach would traverse its fixed-up owner and future inputs.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = offset; index < offset + count; index++)
        {
            context.Entry(nodes[index])
                .State = EntityState.Unchanged;
        }
    }

    /// <summary>Advances the completed wave's real native collection baseline before detaching its members.</summary>
    private static void CompleteWave(
        SnapshotContext context,
        PreparedImport prepared,
        int offset,
        int count
    )
    {
        // WHY: Completed detached rows remain in EF's relationship snapshot while future rows stay CLR-only.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = offset; index < offset + count; index++)
        {
            var node = prepared.Nodes[index];
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(prepared.OwnerEntry, prepared.Navigation, node);
            context.Entry(node)
                .State = EntityState.Detached;
        }
    }

    /// <summary>Installs an equal native reference set with a large detached prefix and one tracked wave.</summary>
    private static void InstallCompletedSnapshot(
        SnapshotContext context,
        PreparedImport prepared
    )
    {
        foreach (var node in prepared.Nodes)
        {
            NestedSetInsertionContract.Instance.AddCollectionSnapshot(prepared.OwnerEntry, prepared.Navigation, node);
        }

        TrackWave(context, prepared.Nodes, prepared.Nodes.Length - BatchSize, BatchSize);
    }

    /// <summary>Observes the real synchronous guard once without an implicit change-detection pass.</summary>
    private static int Observe(
        SnapshotContext context
    )
    {
        return NestedSetSaveChanges.HasHierarchyChanges(context)
            ? 1
            : 0;
    }

    /// <summary>Keeps actual EF metadata and the validated plan proof alive for one isolated observation.</summary>
    private sealed record PreparedImport(
        ProbeOwner Owner,
        IUpdateEntry OwnerEntry,
        ProbeNode[] Nodes,
        INavigation Navigation,
        NestedSetImportInputs Inputs
    );
}
