namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Retains exact application entries without incidental automatic detection.</summary>
    private sealed record ComplexCallbackGraph(
        EntityEntry<ComplexCallbackRow> Row,
        EntityEntry<NavigationNote> Sentinel
    );

    /// <summary>Retains caller object references and copied values before callback mutation.</summary>
    private sealed record ComplexCallbackClrState(
        ComplexCallbackRow Row,
        List<ComplexCallbackItem>? Items,
        ComplexCallbackItemState[] ItemValues,
        ComplexCallbackAddress? Address,
        string? AddressText,
        List<ComplexCallbackLeaf>? Tags,
        ComplexCallbackLeafState[] TagValues
    );

    /// <summary>Retains each element and its nested list without retaining mutable value aliases.</summary>
    private sealed record ComplexCallbackItemState(
        ComplexCallbackItem Item,
        string Text,
        List<ComplexCallbackLeaf>? Parts,
        ComplexCallbackLeafState[] PartValues
    );

    /// <summary>Copies a nested element's value while retaining its caller-owned object reference.</summary>
    private sealed record ComplexCallbackLeafState(
        ComplexCallbackLeaf Leaf,
        string Text
    );

    /// <summary>Copies actual EF scalar, original and modification state at a bounded fixture path.</summary>
    private sealed record ComplexCallbackNativeState(
        string Path,
        object? Instance,
        EntityState? State,
        bool Modified,
        ComplexPeerLeafState[] Leaves,
        ComplexPeerOriginalLeafState[] Originals
    );

    /// <summary>Copies every stored ordinary payload component relevant to recovery.</summary>
    private sealed record ComplexCallbackStoredRow(
        string Label,
        int Version,
        string? Address,
        string? Items,
        string? Tags
    );

    /// <summary>Mutates a captured ordinary callback write only after provider completion, then fails once.</summary>
    private sealed class ComplexCallbackCompletionFailure : SaveChangesInterceptor
    {
        private readonly Action _mutation;

        /// <summary>Retains the observation inputs for the fixture operation.</summary>
        /// <param name="mutation">The callback applied at the observed save boundary.</param>
        public ComplexCallbackCompletionFailure(
            Action mutation
        )
        {
            _mutation = mutation;
        }

        private bool _failed;

        /// <summary>Gets the exact original failure expected to survive complete rollback.</summary>
        internal InvalidOperationException Error { get; } = new("Injected complex callback completion failure.");

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!_failed)
            {
                _failed = true;
                _mutation();

                throw Error;
            }

            return ValueTask.FromResult(result);
        }
    }
}
