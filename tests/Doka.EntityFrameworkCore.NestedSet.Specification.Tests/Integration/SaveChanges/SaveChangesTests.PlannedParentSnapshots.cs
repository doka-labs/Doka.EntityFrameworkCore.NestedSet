using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.NestedSet.Tests;

public abstract partial class SaveChangesTests
{
    /// <summary>
    ///     Constructs the caller's graph without letting a public entry accessor normalize it prematurely.
    /// </summary>
    private static async Task<PlannedParentGraph> PreparePlannedParentAsync(
        DbContext context,
        string navigation,
        bool automaticDetection,
        bool pendingPayload,
        bool normalizeAutomatic
    )
    {
        var nodes = await context.Set<NavigationNode>().Include(node => node.Children).Include(node => node.ScopeOwner)
            .ToArrayAsync(CancellationToken.None);

        var root = nodes.Single(node => node.Id == 1);
        var child = nodes.Single(node => node.Id == 2);
        var target = nodes.Single(node => node.Id == 3);
        var note = await context.Set<NavigationNote>().SingleAsync(CancellationToken.None);
        var updates = context.GetService<IUpdateAdapterFactory>().Create();

        // WHY: Warm native dependent indexes while keys are unchanged, before any caller-side raw FK edit.
        foreach (var entry in updates.Entries)
        {
            foreach (var foreignKey in entry.EntityType.GetReferencingForeignKeys())
            {
                _ = updates.GetDependents(entry, foreignKey).ToArray();
            }
        }

        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var noteValue = context.Entry(note).Property(value => value.Value);
        child.ParentId = target.Id;
        context.Entry(child).Property(node => node.ParentId).IsModified = true;

        if (navigation is "reference" or "both")
        {
            child.Parent = target;
        }

        if (navigation is "add" or "collections" or "both")
        {
            target.Children.Add(child);
        }

        if (navigation is "remove" or "collections" or "both")
        {
            root.Children.Remove(child);
        }

        if (pendingPayload)
        {
            noteValue.CurrentValue = "planned payload";
        }

        context.ChangeTracker.AutoDetectChangesEnabled = automaticDetection;

        if (automaticDetection && normalizeAutomatic)
        {
            // WHY: Automatic rollback baselines describe detected state, as the existing tracker suite does.
            context.ChangeTracker.DetectChanges();
        }

        return new PlannedParentGraph(root, child, target, noteValue);
    }

    /// <summary>Captures public EF state without any ChangeTracker or EntityEntry detection call.</summary>
    private static PlannedEntryState[] CapturePlannedParentState(DbContext context)
    {
        var updates = context.GetService<IUpdateAdapterFactory>().Create();

        return updates.Entries
            .OrderBy(entry => entry.EntityType.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.GetCurrentValue<int>(entry.EntityType.FindProperty("Id")!))
            .Select(entry => new PlannedEntryState(
                entry.ToEntityEntry().Entity,
                entry.EntityState,
                entry.EntityType.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new PlannedPropertyState(
                        property.Name,
                        entry.GetCurrentValue(property),
                        entry.GetOriginalValue(property),
                        entry.IsModified(property),
                        entry.IsConceptualNull(property),
                        property.IsKey() || property.IsForeignKey()
                            ? entry.GetRelationshipSnapshotValue(property)
                            : null)).ToArray(),
                entry.EntityType.GetNavigations().OrderBy(navigation => navigation.Name, StringComparer.Ordinal)
                    .Select(navigation =>
                    {
                        var current = entry.GetCurrentValue(navigation);
                        var relationship = entry.GetRelationshipSnapshotValue(navigation);

                        return new PlannedNavigationState(
                            navigation.Name,
                            current,
                            navigation.IsCollection ? null : relationship,
                            navigation.IsCollection ? CopyPlannedMembers(current) : [],
                            navigation.IsCollection ? CopyPlannedMembers(relationship) : []);
                    }).ToArray(),
                entry.EntityType.GetForeignKeys()
                    .OrderBy(ForeignKeyOrder, StringComparer.Ordinal)
                    .Select(foreignKey => new PlannedPrincipalState(foreignKey,
                        updates.FindPrincipal(entry, foreignKey)?.ToEntityEntry().Entity)).ToArray(),
                entry.EntityType.GetReferencingForeignKeys()
                    .OrderBy(ForeignKeyOrder, StringComparer.Ordinal)
                    .Select(foreignKey => new PlannedDependentsState(foreignKey,
                        updates.GetDependents(entry, foreignKey).Select(dependent => dependent.ToEntityEntry().Entity)
                            .ToArray())).ToArray())).ToArray();
    }

    /// <summary>
    ///     Orders each fixture FK by its declared mapping and scalar names, without reading entity values.
    /// </summary>
    private static string ForeignKeyOrder(IForeignKey foreignKey)
        => foreignKey.DeclaringEntityType.Name + ":" + foreignKey.PrincipalEntityType.Name + ":"
            + string.Join(",", foreignKey.Properties.Select(property => property.Name));

    /// <summary>
    ///     Freezes collection references so a later retry cannot change the captured failed-state evidence.
    /// </summary>
    private static object[] CopyPlannedMembers(object? value) => value is IEnumerable<object> members
        ? members.ToArray()
        : [];

    /// <summary>
    ///     Compares CLR list order exactly and EF relationship collections by their reference-set contract.
    /// </summary>
    private static void AssertPlannedParentState(PlannedEntryState[] expected, PlannedEntryState[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);

        // WHY: Both snapshots are sorted identically, so compare each entity with its exact counterpart.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < expected.Length; index++)
        {
            var left = expected[index];
            var right = actual[index];
            Assert.Same(left.Entity, right.Entity);
            Assert.Equal(left.State, right.State);
            Assert.Equal(left.Properties, right.Properties);
            Assert.Equal(left.Navigations.Length, right.Navigations.Length);
            Assert.Equal(left.Principals, right.Principals);
            Assert.Equal(left.Dependents.Length, right.Dependents.Length);

            // WHY: The same metadata position identifies the navigation in each paired snapshot.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var navigation = 0; navigation < left.Navigations.Length; navigation++)
            {
                var before = left.Navigations[navigation];
                var after = right.Navigations[navigation];
                Assert.Equal(before.Name, after.Name);
                Assert.Same(before.CurrentReference, after.CurrentReference);
                Assert.Same(before.RelationshipReference, after.RelationshipReference);
                Assert.Equal(before.CurrentMembers, after.CurrentMembers, ReferenceEqualityComparer.Instance);
                Assert.True(before.RelationshipMembers.ToHashSet(ReferenceEqualityComparer.Instance)
                    .SetEquals(after.RelationshipMembers));
            }

            // WHY: Native dependent maps are unordered; reference membership must survive rollback exactly.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var foreignKey = 0; foreignKey < left.Dependents.Length; foreignKey++)
            {
                Assert.Same(left.Dependents[foreignKey].ForeignKey, right.Dependents[foreignKey].ForeignKey);
                Assert.True(left.Dependents[foreignKey].Entries.ToHashSet(ReferenceEqualityComparer.Instance)
                    .SetEquals(right.Dependents[foreignKey].Entries));
            }
        }
    }

    /// <summary>
    ///     Creates the destination tree without loading any hierarchy input into the later facade context.
    /// </summary>
    private static async Task SeedPreparedNavigationDestinationAsync(TestDatabase database)
    {
        await using var setup = await CreateNavigationContextAsync(database, wrapped: true);
        await setup.NestedSet<NavigationNode>().ForScope(1)
            .InsertRootAsync(new NavigationNode { Id = 100, Name = "Destination" }, s_manualDestinationTree,
                CancellationToken.None);
    }

    /// <summary>Reuses the existing insertion-shape data with the navigation model's explicit scope.</summary>
    private static NavigationNode[] PreparedNavigationInputs(string shape, int scope)
        => ManualInsertionInputs(shape).Select(input => new NavigationNode
        {
            Id = input.Id,
            Scope = scope,
            Name = input.Name ?? throw new InvalidOperationException("The insertion fixture requires names."),
        }).ToArray();

    /// <summary>Executes the three existing public insertion forms using navigation-mapped inputs.</summary>
    private static async Task InsertPreparedNavigationShapeAsync(
        DbContext context,
        string shape,
        NavigationNode[] inputs
    )
    {
        var tree = context.NestedSet<NavigationNode>().ForScope(inputs[0].Scope);

        switch (shape)
        {
            case "single":
                await tree.InsertChildAsync(inputs[0], 100, CancellationToken.None);
                break;
            case "subtree":
                await tree.InsertSubtreeAsync(
                    new NestedSetBranch<NavigationNode>(inputs[0], [new NestedSetBranch<NavigationNode>(inputs[1])]),
                    100, CancellationToken.None);
                break;
            case "forest":
                await tree.InsertForestAsync(
                    [
                        new NestedSetTreeImport<NavigationNode, Guid>(s_manualFirstImportTree,
                            new NestedSetBranch<NavigationNode>(inputs[0],
                                [new NestedSetBranch<NavigationNode>(inputs[1])])),
                        new NestedSetTreeImport<NavigationNode, Guid>(s_manualSecondImportTree,
                            new NestedSetBranch<NavigationNode>(inputs[2],
                                [new NestedSetBranch<NavigationNode>(inputs[3])])),
                    ], CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    /// <summary>Holds the live graph and one already captured ordinary property entry for follow-up work.</summary>
    private sealed record PlannedParentGraph(
        NavigationNode Root,
        NavigationNode Child,
        NavigationNode Target,
        PropertyEntry<NavigationNote, string> NoteValue);

    /// <summary>Freezes one tracked entity's scalar and relationship state.</summary>
    private sealed record PlannedEntryState(
        object Entity,
        EntityState State,
        PlannedPropertyState[] Properties,
        PlannedNavigationState[] Navigations,
        PlannedPrincipalState[] Principals,
        PlannedDependentsState[] Dependents);

    /// <summary>Freezes immutable scalar values, modification flags and installed relationship identity.</summary>
    private sealed record PlannedPropertyState(
        string Name,
        object? Current,
        object? Original,
        bool Modified,
        bool ConceptualNull,
        object? Relationship);

    /// <summary>Separates observable CLR sequence state from EF's unordered relationship snapshot.</summary>
    private sealed record PlannedNavigationState(
        string Name,
        object? CurrentReference,
        object? RelationshipReference,
        object[] CurrentMembers,
        object[] RelationshipMembers);

    /// <summary>Captures principal lookup through EF's native identity maps.</summary>
    private sealed record PlannedPrincipalState(IForeignKey ForeignKey, object? Entity);

    /// <summary>Captures dependent lookup through EF's native dependent indexes.</summary>
    private sealed record PlannedDependentsState(IForeignKey ForeignKey, object[] Entries);

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
