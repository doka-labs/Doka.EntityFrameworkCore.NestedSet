using Microsoft.EntityFrameworkCore.Update;
using ProbeOwner = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.ProbeOwner;
using SnapshotContext = Doka.EntityFrameworkCore.NestedSet.Tests.HierarchyChangeDetectionTests.SnapshotContext;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks persisted-FK observation when the native write set needs no relationship proof.</summary>
public sealed class PersistedRelationshipClosureTests
{
    /// <summary>Skipped native write entries retain their original state without relationship-completion work.</summary>
    /// <param name="nodes">The number of entries in the actual EF update adapter.</param>
    /// <param name="hierarchy">Whether entries are new hierarchy nodes or modified ordinary owners.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(128, false)]
    [InlineData(8000, false)]
    [InlineData(1, true)]
    [InlineData(128, true)]
    [InlineData(8000, true)]
    public async Task CapturePersisted_SkippedWriteEntries_PreserveNativeWriteSet(
        int nodes,
        bool hierarchy
    )
    {
        // Arrange
        await using var context = new SnapshotContext();

        if (hierarchy)
        {
            var entities = Enumerable
                .Range(1, nodes)
                .Select(id => HierarchyChangeDetectionTests.CreateNode(id))
                .ToArray();

            await context.AddRangeAsync(entities, CancellationToken.None);
        }
        else
        {
            var owners = Enumerable
                .Range(1, nodes)
                .Select(id => new ProbeOwner { Id = id })
                .ToArray();

            context.AttachRange(owners);

            foreach (var owner in owners)
            {
                context
                    .Entry(owner)
                    .Property(value => value.Payload)
                    .CurrentValue = "written payload";
            }
        }

        var entries = context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .ToArray();

        // Act
        var completion = NestedSetCollectionBaseline.CapturePersisted(entries);

        // Assert
        Assert.Equal(nodes, entries.Length);
        Assert.Null(completion);
        Assert.All(
            entries,
            entry => Assert.Equal(
                hierarchy
                    ? EntityState.Added
                    : EntityState.Modified,
                entry.EntityState));

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>An owner already in the insertion journal retains its original captured membership.</summary>
    [Fact]
    public async Task Capture_KnownOwnerCapture_PreservesExistingJournal()
    {
        // Arrange
        await using var context = new SnapshotContext();
        var node = HierarchyChangeDetectionTests.CreateNode(2);
        var owner = new ProbeOwner { Id = node.Scope };
        context.Attach(owner);
        var entry = context.Attach(node);
        NestedSetCollectionBaseline? baseline = null;
        NestedSetCollectionBaseline.CaptureBeforeTracking(entry, ref baseline);
        var initial = baseline;
        var update = NestedSetInsertionContract.Instance.Entry(context.Entry(owner));
        var navigation = update.EntityType.FindNavigation(nameof(ProbeOwner.ScopeNodes))!;

        // WHY: Bind the existing journal operation once; the test exercises the same already-captured native owner.
        var capture = typeof(NestedSetCollectionBaseline).GetMethod(
                "Capture",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<IUpdateEntry, INavigation, HashSet<object>?>>(baseline);

        var members = owner.ScopeNodes.ToArray();

        // Act
        capture(update, navigation, null);

        // Assert
        Assert.NotNull(initial);
        Assert.Same(initial, baseline);
        Assert.Equal(members, owner.ScopeNodes);
        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
