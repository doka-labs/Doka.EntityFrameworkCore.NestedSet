namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>Holds the live graph and one already captured ordinary property entry for follow-up work.</summary>
    private sealed record PlannedParentGraph(
        NavigationNode Root,
        NavigationNode Child,
        NavigationNode Target,
        PropertyEntry<NavigationNote, string> NoteValue
    );

    /// <summary>Freezes one tracked entity's scalar and relationship state.</summary>
    private sealed record PlannedEntryState(
        object Entity,
        EntityState State,
        PlannedPropertyState[] Properties,
        PlannedNavigationState[] Navigations,
        PlannedPrincipalState[] Principals,
        PlannedDependentsState[] Dependents
    );

    /// <summary>Freezes immutable scalar values, modification flags and installed relationship identity.</summary>
    private sealed record PlannedPropertyState(
        string Name,
        object? Current,
        object? Original,
        bool Modified,
        bool ConceptualNull,
        object? Relationship
    );

    /// <summary>Separates observable CLR sequence state from EF's unordered relationship snapshot.</summary>
    private sealed record PlannedNavigationState(
        string Name,
        object? CurrentReference,
        object? RelationshipReference,
        object[] CurrentMembers,
        object[] RelationshipMembers
    );

    /// <summary>Captures principal lookup through EF's native identity maps.</summary>
    private sealed record PlannedPrincipalState(
        IForeignKey ForeignKey,
        object? Entity
    );

    /// <summary>Captures dependent lookup through EF's native dependent indexes.</summary>
    private sealed record PlannedDependentsState(
        IForeignKey ForeignKey,
        object[] Entries
    );

    /// <summary>Fails the first completed native payload save and permits an unchanged retry.</summary>
    private sealed class PlannedParentFailure : SaveChangesInterceptor
    {
        private bool _failed;

        /// <summary>Gets the exact exception that proves the first attempt reached the injected callback.</summary>
        internal Exception Error { get; } = new InvalidOperationException("Injected planned parent callback failure.");

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

                throw Error;
            }

            return ValueTask.FromResult(result);
        }
    }
}
