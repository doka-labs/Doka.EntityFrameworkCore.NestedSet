using TopologyDetails = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyDetails;
using TopologyItem = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyItem;
using TopologyLeaf = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyLeaf;
using TopologyPayload = Doka.EntityFrameworkCore.NestedSet.Tests.ComplexTopologyRollbackTests.TopologyPayload;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public sealed partial class ComplexTopologyGraphRecoveryTests
{
    /// <summary>Builds independent complex instances and nested list storage for one physical entity.</summary>
    internal static TopologyPayload CreatePayload(
        int rows
    )
        => new()
        {
            Id = 7,
            Rows = Enumerable
                .Range(0, rows)
                .Select(index => new TopologyItem
                {
                    Text = $"Stored item {index}",
                    Details = new TopologyDetails
                    {
                        Text = $"Stored details {index}",
                        Code = $"Code {index}",
                    },
                    Leaves =
                    [
                        new TopologyLeaf { Text = $"Stored first leaf {index}" },
                        new TopologyLeaf { Text = $"Stored second leaf {index}" },
                    ],
                })
                .ToList(),
        };

    /// <summary>Separates pending flags and raw unmarked complex values from their stored originals.</summary>
    internal static void MarkPending(
        EntityEntry<TopologyPayload> entry
    )
    {
        var payload = entry.Entity;
        payload.Marker = "Pending marker";
        entry.Property(value => value.Marker)
            .IsModified = true;

        payload.Rows![0].Text = "Pending first item";
        entry
            .ComplexCollection(nameof(TopologyPayload.Rows))[0]
            .Property(nameof(TopologyItem.Text))
            .IsModified = true;

        payload.Optional!.Text = "Raw unmarked optional";
        payload.Rows[^1].Details!.Code = "Raw unmarked final code";
    }

    /// <summary>Records scalar current/original values and flags before callbacks mutate any container.</summary>
    internal static ScalarObservation[] ObserveScalars(
        EntityEntry<TopologyPayload> entry
    )
    {
        List<ScalarObservation> result = [];

        void Add(
            PropertyEntry property
        )
            => result.Add(new ScalarObservation(property.CurrentValue, property.OriginalValue, property.IsModified));

        foreach (var property in entry.Properties)
        {
            Add(property);
        }

        foreach (var property in entry.ComplexProperty(value => value.Optional)
            .Properties)
        {
            Add(property);
        }

        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var rows = entry.Entity.Rows!;

        // WHY: Native complex entries correspond to stable original ordinals, including each nested leaf slot.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows.Count; index++)
        {
            var element = collection[index];
            Add(element.Property(nameof(TopologyItem.Text)));

            foreach (var property in element.ComplexProperty(nameof(TopologyItem.Details))
                .Properties)
            {
                Add(property);
            }

            var nested = element.ComplexCollection(nameof(TopologyItem.Leaves));

            // WHY: Pair the native leaf entry with the corresponding immutable expected scalar observation.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var leaf = 0; leaf < rows[index].Leaves!.Count; leaf++)
            {
                Add(
                    nested[leaf]
                        .Property(nameof(TopologyLeaf.Text)));
            }
        }

        return result.ToArray();
    }

    /// <summary>Records independent aggregate flags and native element states without a mutable oracle.</summary>
    internal static ComplexObservation[] ObserveStates(
        EntityEntry<TopologyPayload> entry
    )
    {
        var collection = entry.ComplexCollection(nameof(TopologyPayload.Rows));
        var result = new List<ComplexObservation>
        {
            new(
                entry.State,
                entry.ComplexProperty(value => value.Optional)
                    .IsModified),
            new(null, collection.IsModified),
        };

        var rows = entry.Entity.Rows!;

        // WHY: A shrinking callback can shift native indices; retain the state expected at every original slot.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows.Count; index++)
        {
            var element = collection[index];
            result.Add(new ComplexObservation(element.State, element.IsModified));
            result.Add(
                new ComplexObservation(
                    null,
                    element.ComplexProperty(nameof(TopologyItem.Details))
                        .IsModified));

            var nested = element.ComplexCollection(nameof(TopologyItem.Leaves));
            result.Add(new ComplexObservation(null, nested.IsModified));

            // WHY: Element state is independent of each property's flag and must survive nested topology loss.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var leaf = 0; leaf < rows[index].Leaves!.Count; leaf++)
            {
                result.Add(new ComplexObservation(nested[leaf].State, nested[leaf].IsModified));
            }
        }

        return result.ToArray();
    }

    /// <summary>Keeps immutable expected scalar values separate from the mutable complex CLR graph.</summary>
    internal readonly record struct ScalarObservation(
        object? Current,
        object? Original,
        bool Modified
    );

    /// <summary>Retains native states and flags without keeping a post-callback graph as the expected value.</summary>
    internal readonly record struct ComplexObservation(
        EntityState? State,
        bool Modified
    );

    /// <summary>Distinguishes the intended callback failure from capture or restoration errors.</summary>
    private sealed class TopologyCallbackFailure : Exception;
}
