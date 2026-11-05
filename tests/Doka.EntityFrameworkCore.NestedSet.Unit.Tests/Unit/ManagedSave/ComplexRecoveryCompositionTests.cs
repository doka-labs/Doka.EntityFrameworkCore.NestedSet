using Microsoft.EntityFrameworkCore.Update;
using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;
using TopologyDetails = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyDetails;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

/// <summary>Checks composed callback recovery against an independent exact CLR and native-lane preimage.</summary>
public sealed partial class ComplexRecoveryCompositionTests(
    ITestOutputHelper output
)
{
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
    public async Task ComposedCallbackRestoresExactNativeAndClrPreimage(
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
        output.WriteLine($"Shape={shape}; operations={change}; boundary={boundary}.");

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
    public async Task PendingAddedCompositionRestoresExactNativeAndClrPreimage(
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
        output.WriteLine($"Pending={change}; detectPending={detectPending}; boundary={boundary}.");

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
    public async Task ReviewSeedCompositionRestoresExactNativeLanes(
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

        output.WriteLine($"Reduced review seed={seed}; pending outer addition and explicit callback operations.");

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

    /// <summary>Installs originals before pending CLR values and one explicitly marked scalar.</summary>
    private static EntityEntry<TopologyPayload> Track(
        TopologyContext context,
        string shape
    )
    {
        var payload = new TopologyPayload
        {
            Id = 31,
            Rows = shape == "nullable"
                ? [Item("First", 2), Item("Second", null), Item("Third", 0)]
                : [Item("First", 1), Item("Second", 3), Item("Third", 2)],
        };

        var entry = context.Attach(payload);
        _ = context.Model.GetRelationalModel();
        InitializeEntries(entry.ComplexCollection(nameof(TopologyPayload.Rows)), payload.Rows!);
        payload.Marker = "Pending marker";
        payload.Rows![0].Text = "Pending first text";
        payload.Rows![0].Details!.Code = "Pending first code";
        payload.Rows![0].Leaves![0].Text = "Pending first leaf";
        entry.Property(value => value.Marker)
            .IsModified = true;

        return entry;
    }

    /// <summary>Initializes stored ordinals before raw caller changes, without detecting those later changes.</summary>
    private static void InitializeEntries(
        ComplexCollectionEntry collection,
        List<TopologyItem> rows
    )
    {
        // WHY: Public ordinal access establishes only the stored baseline; pending raw additions stay uninstalled.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows.Count; index++)
        {
            var element = collection[index];
            _ = element.Property(nameof(TopologyItem.Text))
                .OriginalValue;

            var leaves = rows[index].Leaves;

            if (leaves is null)
            {
                continue;
            }

            var nested = element.ComplexCollection(nameof(TopologyItem.Leaves));

            // WHY: The nested ordinal baseline must exist even when a later callback removes its outer owner.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
            {
                _ = nested[leafIndex]
                    .Property(nameof(TopologyLeaf.Text))
                    .OriginalValue;
            }
        }
    }

    /// <summary>Builds a uniquely named synthetic element with null, empty or populated nested membership.</summary>
    private static TopologyItem Item(
        string name,
        int? leaves
    )
        => new()
        {
            Text = name,
            Details = new TopologyDetails
            {
                Text = $"{name} details",
                Code = $"{name} code",
            },
            Leaves = leaves is null
                ? null
                : Enumerable
                    .Range(0, leaves.Value)
                    .Select(index => new TopologyLeaf { Text = $"{name} leaf {index}" })
                    .ToList(),
        };

    /// <summary>Applies two operations so ordinal movement and nested membership interact in one callback.</summary>
    private static void Mutate(
        TopologyPayload payload,
        string change
    )
    {
        switch (change)
        {
            case "removeFirstTwice":
                payload.Rows!.RemoveAt(0);
                payload.Rows!.RemoveAt(0);
                break;
            case "nestedAddReverse":
                payload.Rows![0].Leaves!.Add(new TopologyLeaf { Text = "Callback added leaf" });
                payload.Rows!.Reverse();
                break;
            case "nestedAddInsertFirst":
                payload.Rows![0].Leaves!.Add(new TopologyLeaf { Text = "Callback added leaf" });
                payload.Rows!.Insert(0, Item("Callback inserted", 3));
                break;
            case "nestedNullThenNew":
                payload.Rows![0].Leaves = null;
                payload.Rows![1].Leaves = [new TopologyLeaf { Text = "Callback new leaf" }];
                break;
            case "outerNullThenNew":
                payload.Rows = null;
                payload.Rows = [Item("Callback replacement", 1)];
                break;
            case "replaceThenRemove":
                payload.Rows = payload.Rows!.ToList();
                payload.Rows.RemoveAt(0);
                break;
            case "nestedAddThenOuterNull":
                payload.Rows![0].Leaves!.Add(new TopologyLeaf { Text = "Callback hidden leaf" });
                payload.Rows = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>Creates pending membership without implicitly forcing EF detection.</summary>
    private static void AddPending(
        TopologyPayload payload,
        string change
    )
    {
        switch (change)
        {
            case "outerEmptyThenNestedAdd":
                payload.Rows!.Add(Item("Pending item", 0));
                break;
            case "outerNestedThenNestedClear":
            case "outerNestedThenClear":
                payload.Rows!.Add(Item("Pending item", 2));
                break;
            case "insertNestedThenReplace":
                payload.Rows!.Insert(0, Item("Pending item", 2));
                break;
            case "nestedAddThenClear":
            case "nestedAddThenNullReverse":
                payload.Rows![0].Leaves!.Add(new TopologyLeaf { Text = "Pending nested leaf" });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>Changes pending Added owners and children independently of their stored neighbors.</summary>
    private static void MutatePending(
        TopologyPayload payload,
        string change
    )
    {
        switch (change)
        {
            case "outerEmptyThenNestedAdd":
                payload.Rows!.Single(item => item.Text == "Pending item")
                    .Leaves!
                    .Add(new TopologyLeaf { Text = "Callback nested leaf" });

                payload.Rows!.Reverse();
                break;
            case "outerNestedThenNestedClear":
                payload.Rows!.Single(item => item.Text == "Pending item")
                    .Leaves!.Clear();

                payload.Rows!.RemoveAt(0);
                break;
            case "outerNestedThenClear":
            case "nestedAddThenClear":
                payload.Rows![0].Details = null;
                payload.Rows!.Clear();
                break;
            case "insertNestedThenReplace":
                payload.Rows![0].Leaves = [new TopologyLeaf { Text = "Callback replacement leaf" }];
                payload.Rows!.Insert(0, Item("Callback item", 1));
                break;
            case "nestedAddThenNullReverse":
                payload.Rows![0].Leaves = null;
                payload.Rows!.Reverse();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    /// <summary>Models raw callbacks, explicit detection and payload acceptance before rollback.</summary>
    private static void FinishCallback(
        TopologyContext context,
        string boundary
    )
    {
        if (boundary != "raw")
        {
            context.ChangeTracker.DetectChanges();
        }

        if (boundary == "accepted")
        {
            context.ChangeTracker.AcceptAllChanges();
        }
    }

    /// <summary>Gets the actual context-owned provider entry without global change detection.</summary>
    private static IUpdateEntry Write(
        DbContext context,
        object entity
    )
        => context
            .GetService<IUpdateAdapterFactory>()
            .Create()
            .Entries
            .Single(entry => ReferenceEquals(
                entry.ToEntityEntry()
                    .Entity,
                entity));
}
