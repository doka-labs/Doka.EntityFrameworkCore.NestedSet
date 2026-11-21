using Microsoft.EntityFrameworkCore.Update;
using TopologyContext = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyContext;
using TopologyDetails = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyDetails;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexRecoveryCompositionTests
{
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
