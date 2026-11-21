using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks composed callback recovery against an independent exact CLR and native-lane preimage.</summary>
public sealed partial class ComplexRecoveryCompositionTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Uses the test runner's output sink.</summary>
    public ComplexRecoveryCompositionTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Provides bounded nullable-shape and callback-lifecycle combinations.</summary>
    public static IEnumerable<object[]> CallbackCases()
    {
        foreach (var shape in new[] { "nullable", "uneven" })
        {
            foreach (var change in new[]
                {
                    "removeFirstTwice",
                    "nestedAddReverse",
                    "nestedAddInsertFirst",
                    "nestedNullThenNew",
                    "outerNullThenNew",
                    "replaceThenRemove",
                    "nestedAddThenOuterNull",
                })
            {
                foreach (var boundary in new[] { "raw", "detected", "accepted" })
                {
                    yield return [shape, change, boundary];
                }
            }
        }
    }

    /// <summary>Provides pending additions separately from callback additions and subsequent acceptance.</summary>
    public static IEnumerable<object[]> PendingCases()
    {
        foreach (var change in new[]
            {
                "outerEmptyThenNestedAdd",
                "outerNestedThenNestedClear",
                "outerNestedThenClear",
                "insertNestedThenReplace",
                "nestedAddThenClear",
                "nestedAddThenNullReverse",
            })
        {
            foreach (var detectPending in new[] { false, true })
            {
                foreach (var boundary in new[] { "raw", "detected", "accepted" })
                {
                    yield return [change, detectPending, boundary];
                }
            }
        }
    }

    /// <summary>Restores composed removals, replacements and nested changes without losing native lane holes.</summary>
    /// <param name="shape">The independent initial nested collection cardinalities.</param>
    /// <param name="change">The two callback operations whose interaction is under test.</param>
    /// <param name="boundary">The callback's detection or acceptance boundary.</param>
    [Theory]
    [MemberData(nameof(CallbackCases))]
    public async Task Restore_ComposedCallback_RestoresExactNativeAndClrPreimage(
        string shape,
        string change,
        string boundary
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = Track(context, shape);
        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Write(context, entry.Entity)]);

        // WHY: EF public element reads can initialize lazy null slots without detecting pending CLR additions.
        // Compare semantic state across capture separately; exact lane recovery starts before the callback.
        var captured = Observe(entry);
        Mutate(entry.Entity, change);
        FinishCallback(context, boundary);
        _output.WriteLine($"Shape={shape}; operations={change}; boundary={boundary}.");

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        AssertCaptureState(before, captured);
        AssertPreimage(entry, before with { Native = captured.Native });
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Preserves pending Added native elements with uninitialized or uneven nested lanes.</summary>
    /// <param name="change">The pending operation and subsequent callback composition.</param>
    /// <param name="detectPending">Whether the caller explicitly installed the pending native element.</param>
    /// <param name="boundary">The callback's detection or acceptance boundary.</param>
    [Theory]
    [MemberData(nameof(PendingCases))]
    public async Task Restore_PendingAddedComposition_RestoresExactNativeAndClrPreimage(
        string change,
        bool detectPending,
        string boundary
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = Track(context, "nullable");
        AddPending(entry.Entity, change);

        if (detectPending)
        {
            context.ChangeTracker.DetectChanges();
        }

        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Write(context, entry.Entity)]);

        // WHY: Public capture reads can expand lazy null slots; subsequent callback growth must be fully undone.
        var captured = Observe(entry);
        MutatePending(entry.Entity, change);

        // WHY: EF acceptance itself throws for this composed ordinal shape. Recovery must also handle that
        // callback failure; requiring the known exception below keeps unrelated setup failures visible.
        var callbackFailure = Record.Exception(() => FinishCallback(context, boundary));
        _output.WriteLine($"Pending={change}; detectPending={detectPending}; boundary={boundary}.");

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        if (change == "insertNestedThenReplace"
            && detectPending
            && boundary == "accepted")
        {
            Assert.IsType<ArgumentOutOfRangeException>(callbackFailure);
        }
        else
        {
            Assert.Null(callbackFailure);
        }

        AssertCaptureState(before, captured);
        AssertPreimage(entry, before with { Native = captured.Native });
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Retains explicit reduced traces from the independent review's failing deterministic seeds.</summary>
    /// <param name="seed">The review seed identifying the reduced trace, rather than an unbounded random test.</param>
    [Theory]
    [InlineData(199)]
    [InlineData(766)]
    [InlineData(837)]
    [InlineData(1821)]
    [InlineData(1855)]
    [InlineData(3603)]
    public async Task Restore_ReviewSeedComposition_RestoresExactNativeLanes(
        int seed
    )
    {
        // Arrange
        await using var context = new TopologyContext();
        var entry = Track(context, "nullable");
        entry.Entity.Rows!.Add(Item("Pending review item", 0));

        if (seed is 199 or 766 or 837 or 3603)
        {
            context.ChangeTracker.DetectChanges();
        }

        var before = Observe(entry);
        var snapshot = NestedSetTrackerSnapshot.Capture(context, [Write(context, entry.Entity)]);

        // WHY: Retain the exact initialized native preimage before any review-trace callback mutation.
        var captured = Observe(entry);

        switch (seed)
        {
            case 199:
                entry.Entity.Rows!.Single(item => item.Text == "Pending review item")
                    .Leaves!
                    .Add(new TopologyLeaf { Text = "Callback nested addition" });

                context.ChangeTracker.DetectChanges();
                break;
            case 766:
                entry.Entity.Rows![0].Details = null;
                entry.Entity.Rows!.Clear();
                context.ChangeTracker.DetectChanges();
                context.ChangeTracker.DetectChanges();
                break;
            case 837:
                entry.Entity.Rows!.Clear();
                context.ChangeTracker.DetectChanges();
                break;
            case 1821:
                entry.Entity.Rows = entry.Entity.Rows!.ToList();
                entry.Entity.Rows![0].Leaves = entry.Entity.Rows![0].Leaves!.ToList();
                entry
                    .ComplexCollection(nameof(TopologyPayload.Rows))[0]
                    .Property(nameof(TopologyItem.Text))
                    .IsModified = true;

                break;
            case 1855:
                entry
                    .ComplexCollection(nameof(TopologyPayload.Rows))[0]
                    .Property(nameof(TopologyItem.Text))
                    .IsModified = true;

                context.ChangeTracker.DetectChanges();
                context.ChangeTracker.AcceptAllChanges();
                break;
            case 3603:
                entry.Entity.Rows![0].Leaves = [new TopologyLeaf { Text = "Callback replacement" }];
                entry.Entity.Rows!.RemoveAt(0);
                entry.Entity.Rows = null;
                context.ChangeTracker.DetectChanges();
                context.ChangeTracker.AcceptAllChanges();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(seed));
        }

        _output.WriteLine($"Reduced review seed={seed}; pending outer addition and explicit callback operations.");

        // Act
        snapshot.Restore();
        snapshot.Restore();

        // Assert
        AssertCaptureState(before, captured);
        AssertPreimage(entry, before with { Native = captured.Native });
        Assert.Equal(
            ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }
}
