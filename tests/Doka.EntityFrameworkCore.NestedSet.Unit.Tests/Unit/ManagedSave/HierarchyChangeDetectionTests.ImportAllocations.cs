using Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;
using Microsoft.EntityFrameworkCore.Update;
using ProbeNode = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeNode;
using ProbeOwner = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeOwner;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Isolates prepared-owner observation from bulk planning, native tracking and provider allocations.</summary>
[Trait("Category", "Allocation")]
[Collection("Allocation measurements")]
public sealed class HierarchyImportCollectionAllocationTests
{
    private const int BatchSize = 64;
    private readonly ITestOutputHelper _output;

    /// <summary>Retains the measured guard-only values alongside the fixed regression budgets.</summary>
    public HierarchyImportCollectionAllocationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Fixed-size waves do not allocate a new full-owner reference set on every observation.</summary>
    /// <param name="nodes">The complete prepared collection size, including detached future inputs.</param>
    [Theory]
    [InlineData(1_000)]
    [InlineData(2_000)]
    [InlineData(4_000)]
    [InlineData(8_000)]
    public async Task PreparedImportGuardAllocationScalesWithBoundedWaves(
        int nodes
    )
    {
        // Arrange
        await using var context = new SnapshotContext();
        var prepared = Prepare(context, nodes);
        using var managed = NestedSetSaveChanges.EnterManagedSave(context, [], static () => { });
        using var import = NestedSetSaveChanges.EnterImportInputs(context, prepared.Inputs);
        TrackWave(context, prepared.Nodes, 0, BatchSize);
        _ = Observe(context, 10);
        var allocated = 0L;
        var changes = 0;
        var maximumTracked = 0;
        var waves = (nodes + BatchSize - 1) / BatchSize;

        // Act
        // WHY: Only the real synchronous guard belongs to this budget. Native setup and completed-prefix
        // advancement occur between measured intervals, matching a bounded tracker without charging EF setup.
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

            var observation = Observe(context, 1);
            allocated += observation.Allocated;
            changes += observation.Changes;
            CompleteWave(context, prepared, offset, count);
        }

        // Assert
        // WHY: The budget grows with the number of bounded observations, not collection-size times waves.
        // It permits fixed adapter/enumerator work and a 64-entry scratch set, but rejects repeated full sets.
        var maximum = 32_768L + waves * 8_192L;
        var snapshot = Assert.IsAssignableFrom<ISet<object>>(
            prepared.OwnerEntry.GetRelationshipSnapshotValue(prepared.Navigation));

        _output.WriteLine(
            $"Prepared import guard: nodes={nodes}; waves={waves}; allocated={allocated} B; "
            + $"maximum={maximum} B; maximum tracked={maximumTracked}.");

        Assert.InRange(allocated, 0, maximum);
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

    /// <summary>A large detached snapshot prefix does not add a per-call set-comparison bitmap.</summary>
    [Fact]
    public async Task CompletedSnapshotSizeDoesNotIncreaseWarmGuardAllocation()
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
        _ = Observe(smallContext, 10);
        _ = Observe(largeContext, 10);

        // Act
        var smallObservation = Observe(smallContext, 100);
        var largeObservation = Observe(largeContext, 100);

        // Assert
        // WHY: Both contexts have the same 64 tracked dependents and equal current/snapshot reference sets.
        // A full-snapshot bitmap grows at 8,000 members; bounded tracked-removal scratch does not.
        _output.WriteLine(
            $"Completed snapshot guard: passes=100; small={smallObservation.Allocated} B; "
            + $"large={largeObservation.Allocated} B.");

        Assert.InRange(largeObservation.Allocated, 0, smallObservation.Allocated + 4_096);
        Assert.Equal(0, smallObservation.Changes);
        Assert.Equal(0, largeObservation.Changes);
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
    public async Task PreparedImportRejectsForeignCollectionReference(
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
    public async Task PreparedImportPreservesDuplicateReferenceSemantics()
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
    public async Task PreparedImportRejectsTrackedSnapshotMemberRemoval()
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

    /// <summary>Advances only the completed wave's real native collection baseline outside allocation timing.</summary>
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

    /// <summary>Measures only real synchronous guard calls, excluding assertions and runner output.</summary>
    private static (long Allocated, int Changes) Observe(
        SnapshotContext context,
        int repetitions
    )
    {
        var changes = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        // WHY: Indexed repetition excludes a test enumerator from the production guard's allocation evidence.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < repetitions; index++)
        {
            changes += NestedSetSaveChanges.HasHierarchyChanges(context)
                ? 1
                : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return (allocated, changes);
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
