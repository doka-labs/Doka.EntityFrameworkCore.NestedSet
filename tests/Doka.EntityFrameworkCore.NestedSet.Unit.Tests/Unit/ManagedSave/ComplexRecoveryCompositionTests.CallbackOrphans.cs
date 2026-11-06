using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexRecoveryCompositionTests
{
    /// <summary>Removes callback-only native owners absent from the captured nested graph.</summary>
    /// <param name="capturedNull">Whether the captured root collection was null rather than an empty list.</param>
    /// <param name="hide">How the callback hides its newly detected owner before the save fails.</param>
    [Theory]
    [InlineData(false, "keep")]
    [InlineData(false, "remove")]
    [InlineData(false, "clear")]
    [InlineData(false, "null")]
    [InlineData(true, "keep")]
    [InlineData(true, "remove")]
    [InlineData(true, "clear")]
    [InlineData(true, "null")]
    public async Task CallbackOnlyNestedOwnersRestoreAbsentCapturedMembership(
        bool capturedNull,
        string hide
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackEmpty(context, capturedNull);
        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Write(context, entry.Entity)]);
        var captured = Observe(entry);
        var callbackEntries = AddHiddenCallback(context, entry, hide, 1);
        var callbackStates = callbackEntries
            .Select(element => element.State)
            .ToArray();

        var callbackIdentities = CallbackIdentities(callbackEntries);
        output.WriteLine($"CapturedNull={capturedNull}; detected callback nested addition; hide={hide}.");

        // Act
        snapshot.Restore();
        snapshot.Restore();
        var restored = Observe(entry);
        context.ChangeTracker.DetectChanges();

        // Assert
        Assert.All(callbackStates, state => Assert.Equal(EntityState.Added, state));
        AssertCaptureState(before, captured);
        AssertClr(restored.Clr, before.Clr);
        AssertNative(captured.Native, restored.Native, exactLanes: true);
        AssertPreimage(entry, before with { Native = captured.Native });
        AssertUnregisteredCallbacks(
            callbackIdentities,
            Observe(entry)
                .Native);

        Assert.False(
            entry.ComplexCollection(nameof(TopologyPayload.Rows))
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Retains exact empty topology across two callbacks, including the second lost owner.</summary>
    /// <param name="capturedNull">Whether both recoveries must restore null rather than an empty caller list.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedCallbacksDoNotRetainLostNativeOwners(
        bool capturedNull
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = TrackEmpty(context, capturedNull);
        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Write(context, entry.Entity)]);
        var captured = Observe(entry);
        var firstCallback = AddHiddenCallback(context, entry, "clear", 1);
        var firstStates = firstCallback
            .Select(element => element.State)
            .ToArray();

        var firstIdentities = CallbackIdentities(firstCallback);
        output.WriteLine($"Reduced seed3890 shape; capturedNull={capturedNull}; two detected callback additions.");

        // Act
        snapshot.Restore();
        var firstRecovery = Observe(entry);
        context.ChangeTracker.DetectChanges();
        var firstDetected = Observe(entry);
        var secondCallback = AddHiddenCallback(context, entry, "null", 2);
        var secondStates = secondCallback
            .Select(element => element.State)
            .ToArray();

        var secondIdentities = CallbackIdentities(secondCallback);
        snapshot.Restore();
        snapshot.Restore();
        var secondRecovery = Observe(entry);
        context.ChangeTracker.DetectChanges();

        // Assert
        AssertCaptureState(before, captured);
        AssertClr(firstRecovery.Clr, before.Clr);
        AssertNative(captured.Native, firstRecovery.Native, exactLanes: true);
        AssertClr(firstDetected.Clr, before.Clr);
        AssertNative(captured.Native, firstDetected.Native, exactLanes: true);
        AssertClr(secondRecovery.Clr, before.Clr);
        AssertNative(captured.Native, secondRecovery.Native, exactLanes: true);
        AssertPreimage(entry, before with { Native = captured.Native });
        Assert.All(firstStates.Concat(secondStates), state => Assert.Equal(EntityState.Added, state));
        AssertUnregisteredCallbacks(
            firstIdentities.Concat(secondIdentities),
            Observe(entry)
                .Native);

        Assert.False(
            entry.ComplexCollection(nameof(TopologyPayload.Rows))
                .IsModified);

        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Tracks a null or empty root with independently marked scalar payload.</summary>
    private static EntityEntry<TopologyPayload> TrackEmpty(
        TopologyContext context,
        bool capturedNull
    )
    {
        var payload = new TopologyPayload
        {
            Id = 53,
            Rows = capturedNull
                ? null
                : [],
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        payload.Marker = "Pending empty-root marker";
        entry.Property(value => value.Marker)
            .IsModified = true;

        return entry;
    }

    /// <summary>Retains native callback identities before raw removal can hide their current CLR access path.</summary>
    private static ComplexElementEntry[] AddHiddenCallback(
        TopologyContext context,
        EntityEntry<TopologyPayload> entry,
        string hide,
        int round
    )
    {
        entry.Entity.Rows ??= [];
        entry.Entity.Rows.Add(Item($"Callback round {round}", 2));
        context.ChangeTracker.DetectChanges();
        var parent = entry
            .ComplexCollection(nameof(TopologyPayload.Rows))[0];

        var leaves = parent.ComplexCollection(nameof(TopologyItem.Leaves));
        ComplexElementEntry[] retained = [parent, leaves[0], leaves[1]];

        // WHY: Removing after detection leaves real native callback identities behind an absent CLR path.
        // A second DetectChanges here would normalize that adversarial failure boundary before recovery.
        switch (hide)
        {
            case "keep":
                break;
            case "remove":
                entry.Entity.Rows.RemoveAt(0);
                break;
            case "clear":
                entry.Entity.Rows.Clear();
                break;
            case "null":
                entry.Entity.Rows = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(hide));
        }

        return retained;
    }

    /// <summary>Retains callback identity before rollback invalidates callback-created entry handles.</summary>
    private static object[] CallbackIdentities(
        IEnumerable<ComplexElementEntry> entries
    )
        => entries
            .Select(element => s_memberOwner.GetValue(element.Property("Text"))!)
            .ToArray();

    /// <summary>Checks both cache lanes without querying invalidated callback entry handles.</summary>
    private static void AssertUnregisteredCallbacks(
        IEnumerable<object> callbacks,
        NativeObservation[] restored
    )
    {
        // WHY: Callback-only handles are outside the captured tracker preimage and invalid after rollback.
        // Their detached-state representation is not a contract; absence from both reachable lanes is.
        foreach (var identity in callbacks)
        {
            Assert.DoesNotContain(restored, observation => ReferenceEquals(identity, observation.Owner));
        }
    }
}
